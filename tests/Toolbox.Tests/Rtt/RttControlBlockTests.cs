namespace Toolbox.Tests.Rtt;

using System.Text;
using Toolbox.Core.Rtt;

public class RttControlBlockTests
{
    [Fact]
    public void FindSignature_locates_magic_at_offset()
    {
        Assert.Equal(0, RttControlBlock.FindSignature("SEGGER RTT\0"u8));
        Assert.Equal(3, RttControlBlock.FindSignature("abc"u8.ToArray()
            .Concat("SEGGER RTT"u8.ToArray()).ToArray()));
        // Magic ends exactly at the buffer end (no trailing NUL in the dump).
        Assert.Equal(4, RttControlBlock.FindSignature("junkSEGGER RTT"u8));
    }

    [Fact]
    public void FindSignature_returns_negative_when_absent()
    {
        Assert.Equal(-1, RttControlBlock.FindSignature("SEGGER RT"u8));       // truncated
        Assert.Equal(-1, RttControlBlock.FindSignature("segger rtt\0"u8));   // case-sensitive
        Assert.Equal(-1, RttControlBlock.FindSignature([]));
    }

    [Fact]
    public void FindSignature_finds_first_occurrence()
    {
        byte[] dump = "xxSEGGER RTTySEGGER RTT"u8.ToArray();
        Assert.Equal(2, RttControlBlock.FindSignature(dump));
    }
}
