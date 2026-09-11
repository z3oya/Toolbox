namespace Toolbox.Tests.SignalGen;

using System.Net;
using System.Net.Sockets;
using Toolbox.Core.SignalGen;

public class UdpTransportLoopbackTests
{
    [Fact]
    public async Task Packet_round_trips_over_localhost_udp()
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;

        using var transport = new UdpClientTransport("127.0.0.1", port);
        var engine = new SignalEngine(48_000);
        engine.UpdateChannels(new[] { new ChannelConfig { Waveform = WaveformKind.Dc, Amplitude = 0.25 } });
        using var streamer = new SignalStreamer(engine, transport);
        streamer.SendOneBlock();

        var result = await receiver.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(UdpPacketCodec.TryDecode(result.Buffer, out var seq, out _, out var samples));
        Assert.Equal(0u, seq);
        Assert.Equal(streamer.BlockSize, samples.Length);
        Assert.All(samples, s => Assert.Equal(0.25, (double)s, 5));
    }

    [Fact]
    public void Stop_then_dispose_order_is_safe()
    {
        // MainForm shutdown order: Stop drains + guards, so disposing the transport right
        // after must never hit a disposed UdpClient from an in-flight callback.
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;
        using var transport = new UdpClientTransport("127.0.0.1", port);
        var engine = new SignalEngine(48_000);
        engine.UpdateChannels(new[] { new ChannelConfig { Waveform = WaveformKind.Dc, Amplitude = 0.5 } });
        using var streamer = new SignalStreamer(engine, transport);
        streamer.Start();
        streamer.SendOneBlock();
        streamer.Stop();
        streamer.Dispose();
        transport.Dispose(); // must not throw
    }
}
