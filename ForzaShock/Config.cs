using OpenShock.Desktop.ModuleBase.Models;

namespace OpenShock.Desktop.Modules.ForzaShock;

public sealed class ForzaShockModuleConfig
{
    public int UdpPort { get; set; } = 5300;
    public bool AutoStart { get; set; } = false;
    public bool Diagnostics { get; set; } = false;

    public CollisionConfig Collision { get; set; } = new();
    public ShockConfig Wall { get; set; } = new() { MinIntensity = 15, MaxIntensity = 50 };
    public ShockConfig Smashable { get; set; } = new() { MinIntensity = 5, MaxIntensity = 20 };
}

public sealed class CollisionConfig
{
    public float SmashableVelDiffThreshold { get; set; } = 0.05f;
    public float AccelMagnitudeJumpThreshold { get; set; } = 25.0f;
    public float MinSpeedKmh { get; set; } = 10.0f;
    public int CooldownMs { get; set; } = 750;
    public float MaxIntensityAccelMagJump { get; set; } = 80.0f;
    public float MaxIntensitySmashableVelDiff { get; set; } = 8.0f;
}

public sealed class ShockConfig
{
    public List<Guid> ShockerIds { get; set; } = [];
    public ControlType Action { get; set; } = ControlType.Vibrate;
    public byte MinIntensity { get; set; } = 10;
    public byte MaxIntensity { get; set; } = 35;
    public ushort DurationMs { get; set; } = 300;
}
