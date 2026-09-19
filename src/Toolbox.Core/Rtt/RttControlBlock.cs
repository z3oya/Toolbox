namespace Toolbox.Core.Rtt;

/// <summary>The control block identification used when the caller pins a RAM window
/// (RttAddress + RttRange): the "SEGGER RTT" magic preceding the block header.</summary>
public static class RttControlBlock
{
    public static readonly byte[] Signature = "SEGGER RTT"u8.ToArray();

    /// <summary>Offset of the first signature occurrence in a RAM dump, or -1 when absent
    /// (case-sensitive, no partial tail match — same semantics as the reference tool's indexOf).</summary>
    public static int FindSignature(ReadOnlySpan<byte> dump) => dump.IndexOf(Signature);
}
