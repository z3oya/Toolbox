using System.Net.Sockets;

namespace Toolbox.Core.SignalGen;

/// <summary>Sends encoded packets to a fixed IPv4 endpoint (BCL UdpClient, connectionless send;
/// the underlying socket is IPv4-only, so IPv6 literals like "::1" fail at first Send).</summary>
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
    public void Send(ReadOnlySpan<byte> packet) => _client.Send(packet, Host, Port);

    public void Dispose() => _client.Dispose();
}
