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
        if (_loop is { IsCompleted: false })
        {
            _log.LogInformation("Start() called but listener is already running; ignoring.");
            return;
        }

        var c = _config.Config;
        _log.LogInformation(
            "Start(): port={Port} diagnostics={Diag} wall=(count={WallCount} action={WallAction}) smashable=(count={SmashCount} action={SmashAction}) thresholds: velDiff>={Vd} accelJump>={Aj} minSpeed={Ms}km/h cooldown={Cd}ms",
            c.UdpPort, c.Diagnostics,
            c.Wall.ShockerIds.Count, c.Wall.Action,
            c.Smashable.ShockerIds.Count, c.Smashable.Action,
            c.Collision.SmashableVelDiffThreshold, c.Collision.AccelMagnitudeJumpThreshold,
            c.Collision.MinSpeedKmh, c.Collision.CooldownMs);

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
        UdpClient? udp = null;
        try
        {
            udp = new UdpClient(new IPEndPoint(IPAddress.Any, cfg.UdpPort));
            _log.LogInformation("ForzaShock listening on UDP :{Port} diagnostics={Diag}",
                cfg.UdpPort, cfg.Diagnostics);

            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try { result = await udp.ReceiveAsync(ct); }
                catch (OperationCanceledException) { break; }

                if (!ForzaPacket.TryParse(result.Buffer, out var frame))
                {
                    if (PacketsReceived == 0)
                        _log.LogWarning("First UDP packet was {Len} bytes (expected {Expected})",
                            result.Buffer.Length, ForzaPacket.PacketSize);
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
                    _log.LogInformation("Collision: {Kind} {Reason} -> intensity={I:P0} (diagnostics={Diag})",
                        hit.Kind, hit.Reason, hit.Intensity01, cfg.Diagnostics);

                    if (cfg.Diagnostics)
                    {
                        _log.LogInformation(
                            "[DRY-RUN] diagnostics=true, NOT firing. Toggle off in UI to enable. " +
                            "Would have sent ({Kind}): shockers={Count} action={Action} intensity~={I} duration={Dur}ms exclusive={Excl}",
                            hit.Kind, profile.ShockerIds.Count, profile.Action,
                            (byte)Math.Clamp((int)MathF.Round(profile.MinIntensity + hit.Intensity01 * (profile.MaxIntensity - profile.MinIntensity)), 1, 100),
                            profile.DurationMs, profile.Exclusive);
                    }
                    else
                    {
                        await FireAsync(hit.Intensity01, profile);
                    }
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
            udp?.Dispose();
            StateChanged?.Invoke();
        }
    }

    private async Task FireAsync(float intensity01, ShockConfig s)
    {
        _log.LogInformation("FireAsync entry: intensity01={I01} shockerCount={Count} action={Action} min={Min} max={Max} duration={Dur}ms exclusive={Excl}",
            intensity01, s.ShockerIds.Count, s.Action, s.MinIntensity, s.MaxIntensity, s.DurationMs, s.Exclusive);

        if (s.ShockerIds.Count == 0)
        {
            _log.LogWarning("FireAsync: no shocker IDs configured; SKIPPING. Tick at least one shocker in the ForzaShock UI under 'Shockers to fire'.");
            LastError = "No shocker IDs configured";
            return;
        }

        byte intensity = (byte)Math.Clamp(
            (int)MathF.Round(s.MinIntensity + intensity01 * (s.MaxIntensity - s.MinIntensity)),
            1, 100);
        ushort duration = (ushort)Math.Clamp((int)s.DurationMs, 300, 30000);

        _log.LogInformation("FireAsync: scaled intensity={Intensity}/100 duration={Duration}ms; sending to {Count} shocker(s): [{Ids}]",
            intensity, duration, s.ShockerIds.Count, string.Join(", ", s.ShockerIds));

        var shocks = s.ShockerIds.Select(id => new ShockerControl
        {
            Id = id,
            Type = s.Action,
            Intensity = intensity,
            Duration = duration,
            Exclusive = s.Exclusive,
        }).ToList();

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await _control.Control(shocks, "ForzaShock collision");
            sw.Stop();
            _log.LogInformation("FireAsync: IOpenShockControl.Control returned in {Ms}ms (host accepted the command — host-side logs/Discord/hub status will show whether it reached the shocker)",
                sw.ElapsedMilliseconds);
            LastError = null;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "FireAsync: IOpenShockControl.Control threw: {Msg}", ex.Message);
            LastError = ex.Message;
        }
    }

    public Task TestFireAsync(CollisionKind kind, float intensity01 = 0.5f)
    {
        var profile = kind == CollisionKind.Wall ? _config.Config.Wall : _config.Config.Smashable;
        _log.LogInformation("TestFireAsync invoked (kind={Kind} intensity01={I01}) — bypasses detection & diagnostics flag", kind, intensity01);
        return FireAsync(intensity01, profile);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
