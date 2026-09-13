namespace Toolbox.Tests.SerialComm;

using Toolbox.Core.SerialComm;

public class HexCodecTests
{
    [Fact]
    public void TryParse_collects_digits_ignoring_separators_and_case()
    {
        Assert.True(HexCodec.TryParse("aA 55,0x01;1-2", out var bytes, out var error));
        Assert.Null(error);
        Assert.Equal(new byte[] { 0xAA, 0x55, 0x01, 0x12 }, bytes);
    }

    [Fact]
    public void TryParse_accepts_0x_prefixes()
    {
        Assert.True(HexCodec.TryParse("0xAA 0x55 0x01", out var bytes, out var error));
        Assert.Null(error);
        Assert.Equal(new byte[] { 0xAA, 0x55, 0x01 }, bytes);
    }

    [Fact]
    public void TryParse_odd_digit_count_fails_with_error()
    {
        Assert.False(HexCodec.TryParse("ABC", out var bytes, out var error));
        Assert.NotEqual("", error);
        Assert.Empty(bytes);
    }

    [Fact]
    public void TryParse_non_hex_letter_fails_with_error()
    {
        // "G" is a letter, not a separator: silently sending nothing on a typo would be worse than an error.
        Assert.False(HexCodec.TryParse("GG", out var bytes, out var error));
        Assert.NotEqual("", error);
        Assert.Empty(bytes);
    }

    [Fact]
    public void TryParse_empty_and_separator_only_yield_empty_array()
    {
        Assert.True(HexCodec.TryParse("", out var bytes, out var error));
        Assert.Null(error);
        Assert.Empty(bytes);

        Assert.True(HexCodec.TryParse(" ,;-	", out bytes, out error));
        Assert.Null(error);
        Assert.Empty(bytes);
    }

    [Fact]
    public void Format_pads_and_uppercases()
    {
        Assert.Equal("AA 05 10", HexCodec.Format(new byte[] { 0xAA, 0x05, 0x10 }));
    }

    [Fact]
    public void Format_empty_is_empty()
    {
        Assert.Equal("", HexCodec.Format(Array.Empty<byte>()));
    }

    [Fact]
    public void Format_then_TryParse_roundtrips()
    {
        var payload = new byte[] { 0x00, 0x0F, 0xFF, 0x10, 0xA5 };
        Assert.True(HexCodec.TryParse(HexCodec.Format(payload), out var bytes, out _));
        Assert.Equal(payload, bytes);
    }
}
