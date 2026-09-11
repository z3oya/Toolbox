using System.Buffers.Binary;

namespace Toolbox.Core.SignalGen;

/// <summary>Frame layout: Magic "TFG1" (u32 LE) | Seq (u32 LE) | Timestamp (u64 LE) | SampleCount (i32 LE) | float32 LE samples.</summary>
public static class UdpPacketCodec
{
    public const uint Magic = 0x5446_4731; // "TFG1"
    public const int HeaderBytes = 20;

    public static byte[] Encode(uint seq, ulong timestamp, ReadOnlySpan<float> samples)
    {
        var packet = new byte[HeaderBytes + samples.Length * sizeof(float)];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), seq);
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(8), timestamp);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(16), samples.Length);
        for (int i = 0; i < samples.Length; i++) {
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(HeaderBytes + i * sizeof(float)),
                BitConverter.SingleToInt32Bits(samples[i]));
        }
        return packet;
    }

    public static bool TryDecode(ReadOnlySpan<byte> packet, out uint seq, out ulong timestamp, out float[] samples)
    {
        seq = 0; timestamp = 0; samples = Array.Empty<float>();
        if (packet.Length < HeaderBytes) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(packet) != Magic) return false;
        int count = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(16));
        // Upper-bound count before multiplying: a hostile count near int.MaxValue would overflow
        // count * sizeof(float) and could pass the exact-length check (e.g. 0x40000000 wraps to 0).
        if (count < 0 || count > (packet.Length - HeaderBytes) / sizeof(float)
            || packet.Length != HeaderBytes + count * sizeof(float)) return false;
        seq = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(4));
        timestamp = BinaryPrimitives.ReadUInt64LittleEndian(packet.Slice(8));
        samples = new float[count];
        for (int i = 0; i < count; i++) {
            samples[i] = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(HeaderBytes + i * sizeof(float))));
        }
        return true;
    }
}
