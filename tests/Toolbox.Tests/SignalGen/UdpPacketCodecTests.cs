namespace Toolbox.Tests.SignalGen;

using System.Buffers.Binary;
using Toolbox.Core.SignalGen;

public class UdpPacketCodecTests
{
    [Fact]
    public void Encode_writes_header_layout_exactly()
    {
        var packet = UdpPacketCodec.Encode(seq: 7, timestamp: 123_456UL,
            new float[] { 0.5f, -0.25f });
        Assert.Equal(20 + 2 * 4, packet.Length);
        Assert.Equal(0x5446_4731u, BinaryPrimitives.ReadUInt32LittleEndian(packet));          // "TFG1"
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)));
        Assert.Equal(123_456UL, BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(8)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(16)));
        Assert.Equal(0.5f, BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(20))));
    }

    [Fact]
    public void Round_trip_preserves_samples_and_metadata()
    {
        var samples = new float[] { 0f, 1f, -1f, 0.123_456f };
        var packet = UdpPacketCodec.Encode(99, 1UL << 40, samples);
        Assert.True(UdpPacketCodec.TryDecode(packet, out var seq, out var ts, out var decoded));
        Assert.Equal(99u, seq);
        Assert.Equal(1UL << 40, ts);
        Assert.Equal(samples, decoded);
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3 })]                       // too short
    [InlineData(new byte[] { 0x32, 0x47, 0x46, 0x54, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })] // wrong magic (0x54464732)
    public void TryDecode_rejects_malformed_packets(byte[] raw)
        => Assert.False(UdpPacketCodec.TryDecode(raw, out _, out _, out _));

    [Fact]
    public void TryDecode_rejects_count_payload_mismatch()
    {
        var packet = UdpPacketCodec.Encode(0, 0, new float[3]);
        packet[16] = 9; // count inconsistent with payload length
        Assert.False(UdpPacketCodec.TryDecode(packet, out _, out _, out _));
    }

    [Fact]
    public void TryDecode_rejects_hostile_count_overflow()
    {
        // count = 0x40000000: count * sizeof(float) wraps to 0 in int32 arithmetic,
        // so an unguarded length check would pass and allocate a 4 GiB sample array.
        var hostile = new byte[UdpPacketCodec.HeaderBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(hostile, UdpPacketCodec.Magic);
        BinaryPrimitives.WriteInt32LittleEndian(hostile.AsSpan(16), 0x4000_0000);
        Assert.False(UdpPacketCodec.TryDecode(hostile, out _, out _, out _));
    }

    [Fact]
    public void Round_trip_header_only_packet_count_zero()
    {
        // count = 0 sits on the boundary between the short-packet check and the count
        // upper-bound check; a header-only packet must stay valid with empty samples.
        var packet = UdpPacketCodec.Encode(0, 0, ReadOnlySpan<float>.Empty);
        Assert.Equal(UdpPacketCodec.HeaderBytes, packet.Length);
        Assert.True(UdpPacketCodec.TryDecode(packet, out var seq, out var ts, out var samples));
        Assert.Equal(0u, seq);
        Assert.Equal(0UL, ts);
        Assert.Empty(samples);
    }

    [Fact]
    public void Round_trip_preserves_max_sequence_number()
    {
        // seq is an unsigned 32-bit counter: 0xFFFFFFFF must survive intact (not read
        // as -1) — the invariant Task 7's _seq++ wrap-around relies on.
        var packet = UdpPacketCodec.Encode(uint.MaxValue, 0, new float[] { 1f });
        Assert.True(UdpPacketCodec.TryDecode(packet, out var seq, out _, out _));
        Assert.Equal(uint.MaxValue, seq);
    }

    [Fact]
    public void Round_trip_preserves_special_float_bits()
    {
        // Compare integer bit patterns, not float equality: float.Equals treats NaN == NaN
        // and -0.0 == +0.0, so it cannot detect bit normalization by the codec.
        float[] specials = { float.NaN, -0.0f, float.PositiveInfinity };
        foreach (var s in specials)
        {
            var packet = UdpPacketCodec.Encode(0, 0, new[] { s });
            Assert.True(UdpPacketCodec.TryDecode(packet, out _, out _, out var decoded));
            Assert.Equal(BitConverter.SingleToInt32Bits(s), BitConverter.SingleToInt32Bits(decoded[0]));
        }
    }
}
