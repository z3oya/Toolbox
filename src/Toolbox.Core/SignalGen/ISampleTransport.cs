namespace Toolbox.Core.SignalGen;

/// <summary>Sends one encoded packet. Implementations: UDP (UdpClientTransport), test fakes, future TCP/file sinks.
/// Implementations need not be thread-safe; the streamer serializes Send calls under its send lock.</summary>
public interface ISampleTransport
{
    void Send(ReadOnlySpan<byte> packet);
}
