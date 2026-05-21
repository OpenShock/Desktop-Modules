using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using OpenShock.Desktop.ModuleBase.Api;
using OpenShock.Desktop.ModuleBase.Config;
using OpenShock.Desktop.ModuleBase.Models;

namespace OpenShock.Desktop.Modules.ForzaShock;

public sealed class TelemetryListener : IAsyncDisposable
{
    private readonly IModuleConfig<ForzaShockModuleConfig> _config;
    private readonly IOpenShockControl _control;
    private readonly ILogger<TelemetryListener> _log;
    private readonly CollisionDetector _detector;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public bool IsRunning => _loop is { IsCompleted: false };
    public long PacketsReceived { get; private set; }
    public long DetectionsFired { get; private set; }
    public Frame? LastFrame { get; private set; }
    public string? LastReason { get; private set; }
    public DateTimeOffset? LastDetectionAt { get; private set; }
    public string? LastError { get; private set; }

    public const int HistorySamplePeriodMs = 100;
    private long _lastSampleTicks;

    public event Action? StateChanged;
    public event Action<TelemetrySample>? SampleAdded;

    public TelemetryListener(
        IModuleConfig<ForzaShockModuleConfig> config,
        IOpenShockControl control,
        ILogger<TelemetryListener> log)
    {
        _config = config;
        _control = control;
        _log = log;
        _detector = new CollisionDetector(config.Config.Collision);
    }

    public void Start()
    {
        if (_loop is { IsCompleted: false }) return;

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
        StateChanged?.Invoke();
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        _cts.Cancel();
        try { if (_loop is not null) await _loop; }
        catch (OperationCanceledException) { }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _loop = null;
            StateChanged?.Invoke();
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var cfg = _config.Config;
        Socket? socket = null;
        var buffer = new byte[ForzaPacket.PacketSize];
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, cfg.UdpPort));
            _log.LogInformation("Listening on UDP :{Port} (diagnostics={Diag})", cfg.UdpPort, cfg.Diagnostics);

            while (!ct.IsCancellationRequested)
            {
                int received;
                try { received = await socket.ReceiveAsync(buffer, SocketFlags.None, ct); }
                catch (OperationCanceledException) { break; }

                if (!ForzaPacket.TryParse(buffer.AsSpan(0, received), out var frame))
                {
                    if (PacketsReceived == 0)
                        _log.LogWarning("First UDP packet was {Len} bytes (expected {Expected})",
                            received, ForzaPacket.PacketSize);
                    continue;
                }

                PacketsReceived++;
                LastFrame = frame;

                var handler = SampleAdded;
                var nowTicks = Environment.TickCount64;
                if (handler is not null && nowTicks - _lastSampleTicks >= HistorySamplePeriodMs)
                {
                    _lastSampleTicks = nowTicks;
                    handler.Invoke(new TelemetrySample(DateTime.UtcNow, frame.AccelMagnitude, frame.SmashableVelDiff));
                }

                var detection = _detector.Evaluate(frame);
                if (detection is { } hit)
                {
                    DetectionsFired++;
                    LastReason = hit.Reason;
                    LastDetectionAt = DateTimeOffset.Now;
                    var profile = hit.Kind == CollisionKind.Wall ? cfg.Wall : cfg.Smashable;
                    _log.LogInformation("Collision: {Kind} {Reason} -> intensity={I:P0}{Dry}",
                        hit.Kind, hit.Reason, hit.Intensity01, cfg.Diagnostics ? " [DRY-RUN]" : "");

                    if (!cfg.Diagnostics)
                        await FireAsync(hit.Intensity01, profile);
                }

                if (PacketsReceived % 30 == 0 || detection is not null)
                    StateChanged?.Invoke();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex.Message;
            _log.LogError(ex, "ForzaShock listener crashed");
        }
        finally
        {
            socket?.Dispose();
            StateChanged?.Invoke();
        }
    }

    private async Task FireAsync(float intensity01, ShockConfig s)
    {
        if (s.ShockerIds.Count == 0)
        {
            _log.LogWarning("No shockers configured for this profile; skipping.");
            LastError = "No shocker IDs configured";
            return;
        }

        byte intensity = (byte)Math.Clamp(
            (int)MathF.Round(s.MinIntensity + intensity01 * (s.MaxIntensity - s.MinIntensity)),
            1, 100);
        ushort duration = (ushort)Math.Clamp((int)s.DurationMs, 300, 30000);

        var shocks = s.ShockerIds.Select(id => new ShockerControl
        {
            Id = id,
            Type = s.Action,
            Intensity = intensity,
            Duration = duration,
            Exclusive = true,
        }).ToList();

        try
        {
            await _control.Control(shocks, "ForzaShock collision");
            LastError = null;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Control call failed: {Msg}", ex.Message);
            LastError = ex.Message;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
