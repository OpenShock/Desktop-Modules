using System.Buffers.Binary;

namespace OpenShock.Desktop.Modules.ForzaShock;

public readonly record struct TelemetrySample(DateTime At, float AccelMagnitude, float VelDiff);

public readonly record struct Frame(
    bool RaceOn,
    uint TimestampMs,
    float AccelX,
    float AccelY,
    float AccelZ,
    float VelocityX,
    float VelocityY,
    float VelocityZ,
    float SmashableVelDiff,
    float SmashableMass,
    float Speed)
{
    public float AccelMagnitude => MathF.Sqrt(AccelX * AccelX + AccelY * AccelY + AccelZ * AccelZ);
    public float SpeedKmh => Speed * 3.6f;
}

public static class ForzaPacket
{
    public const int PacketSize = 324;

    public static bool TryParse(ReadOnlySpan<byte> buf, out Frame frame)
    {
        if (buf.Length < PacketSize)
        {
            frame = default;
            return false;
        }

        frame = new Frame(
            RaceOn: BinaryPrimitives.ReadInt32LittleEndian(buf[0..]) != 0,
            TimestampMs: BinaryPrimitives.ReadUInt32LittleEndian(buf[4..]),
            AccelX: BinaryPrimitives.ReadSingleLittleEndian(buf[20..]),
            AccelY: BinaryPrimitives.ReadSingleLittleEndian(buf[24..]),
            AccelZ: BinaryPrimitives.ReadSingleLittleEndian(buf[28..]),
            VelocityX: BinaryPrimitives.ReadSingleLittleEndian(buf[32..]),
            VelocityY: BinaryPrimitives.ReadSingleLittleEndian(buf[36..]),
            VelocityZ: BinaryPrimitives.ReadSingleLittleEndian(buf[40..]),
            SmashableVelDiff: BinaryPrimitives.ReadSingleLittleEndian(buf[236..]),
            SmashableMass: BinaryPrimitives.ReadSingleLittleEndian(buf[240..]),
            Speed: BinaryPrimitives.ReadSingleLittleEndian(buf[256..]));
        return true;
    }
}
