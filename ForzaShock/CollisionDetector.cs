namespace OpenShock.Desktop.Modules.ForzaShock;

public enum CollisionKind
{
    Smashable,
    Wall,
}

public readonly record struct Detection(float Intensity01, string Reason, CollisionKind Kind);

public sealed class CollisionDetector
{
    private readonly CollisionConfig _cfg;
    private readonly TimeProvider _time;

    private bool _wasRaceOn;
    private bool _swallowNextFrame;
    private float _lastAccelMag;
    private long _cooldownUntilTicks;

    public CollisionDetector(CollisionConfig cfg, TimeProvider? time = null)
    {
        _cfg = cfg;
        _time = time ?? TimeProvider.System;
    }

    public Detection? Evaluate(in Frame frame)
    {
        if (!frame.RaceOn)
        {
            _wasRaceOn = false;
            _swallowNextFrame = false;
            _lastAccelMag = 0;
            return null;
        }

        if (!_wasRaceOn)
        {
            _wasRaceOn = true;
            _swallowNextFrame = true;
            _lastAccelMag = frame.AccelMagnitude;
            return null;
        }

        float currentAccelMag = frame.AccelMagnitude;
        float accelJump = currentAccelMag - _lastAccelMag;
        _lastAccelMag = currentAccelMag;

        if (_swallowNextFrame)
        {
            _swallowNextFrame = false;
            return null;
        }

        long nowTicks = _time.GetUtcNow().UtcTicks;
        if (nowTicks < _cooldownUntilTicks) return null;
        if (frame.SpeedKmh < _cfg.MinSpeedKmh) return null;

        float velDiff = MathF.Abs(frame.SmashableVelDiff);
        bool smashableHit = velDiff >= _cfg.SmashableVelDiffThreshold;
        bool accelSpike = accelJump >= _cfg.AccelMagnitudeJumpThreshold;
        if (!smashableHit && !accelSpike) return null;

        float intensity;
        string reason;
        CollisionKind kind;
        if (smashableHit && (!accelSpike || velDiff / _cfg.MaxIntensitySmashableVelDiff
                                            >= accelJump / _cfg.MaxIntensityAccelMagJump))
        {
            intensity = Math.Clamp(velDiff / _cfg.MaxIntensitySmashableVelDiff, 0f, 1f);
            reason = $"smashable velDiff={velDiff:F2} m/s mass={frame.SmashableMass:F2}";
            kind = CollisionKind.Smashable;
        }
        else
        {
            intensity = Math.Clamp(accelJump / _cfg.MaxIntensityAccelMagJump, 0f, 1f);
            reason = $"wall accel jump={accelJump:F1} m/s^2";
            kind = CollisionKind.Wall;
        }

        _cooldownUntilTicks = nowTicks + TimeSpan.FromMilliseconds(_cfg.CooldownMs).Ticks;
        return new Detection(intensity, reason, kind);
    }
}
