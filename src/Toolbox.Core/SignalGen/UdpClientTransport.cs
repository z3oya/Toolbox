using System.Net.Sockets;

namespace Toolbox.Core.SignalGen;

/// <summary>Sends encoded packets to a fixed IPv4 endpoint (BCL UdpClient, connectionless send;
/// the underlying socket is IPv4-only, so IPv6 literals like "::1" fail at first Send).
/// A failed datagram is dropped, not thrown (see <see cref="Send"/>).</summary>
public sealed class UdpClientTransport : ISampleTransport, IDisposable
{
    private readonly UdpClient _client;

    public string Host { get; }
    public int Port { get; }

    public UdpClientTransport(string host, int port)
    {
        Host = host;
        Port = port;
        _client = new UdpClient();
    }

    // The endpoint-less Send(ReadOnlySpan<byte>) overload requires a Connect()ed client; the
    // connectionless host/port form keeps sends stateless (DNS resolves per send, no eager ctor work).
    public void Send(ReadOnlySpan<byte> packet)
    {
        // Windows surfaces ICMP port-unreachable from a previous send as WSAECONNRESET on this one.
        // The streamer invokes this on a timer thread where an unhandled exception would kill the
        // process, and a missing receiver is an expected transient for a fire-and-forget UDP stream —
        // so a failed datagram is dropped, not thrown. Packet counters still advance (they count
        // rendered-and-attempted blocks). Not unit-tested: triggering the ICMP round-trip
        // deterministically is platform/timing-dependent.
        try
        {
            _client.Send(packet, Host, Port);
        }
        catch (SocketException)
        {
        }
    }

    public void Dispose() => _client.Dispose();
}
