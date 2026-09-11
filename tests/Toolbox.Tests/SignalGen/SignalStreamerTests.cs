namespace Toolbox.Tests.SignalGen;

using System.Diagnostics;
using Toolbox.Core.SignalGen;

public class SignalStreamerTests
{
    private sealed class FakeTransport : ISampleTransport
    {
        public List<byte[]> Sent { get; } = new();
        public void Send(ReadOnlySpan<byte> packet) => Sent.Add(packet.ToArray());
    }

    /// <summary>Manual clock: CreateTimer captures the callback, the test fires it by hand via <see cref="Fire"/>;
    /// base TimestampFrequency (Stopwatch.Frequency) matches the streamer's period math.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _now = 1_000_000;
        public Action? Fire; // set by CreateTimer; test invokes it manually
        public override long GetTimestamp() => _now;
        public void Advance(long delta) => _now += delta;
        public override ITimer CreateTimer(System.Threading.TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Fire = () => callback(state);
            return new ManualTimer();
        }
        private sealed class ManualTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [Fact]
    public void SendOneBlock_encodes_engine_output_and_increments_seq()
    {
        var engine = new SignalEngine(48_000);
        engine.UpdateChannels(new[] { new ChannelConfig { Waveform = WaveformKind.Dc, Amplitude = 0.25 } });
        var transport = new FakeTransport();
        using var streamer = new SignalStreamer(engine, transport);

        streamer.SendOneBlock();
        streamer.SendOneBlock();

        Assert.Equal(2, transport.Sent.Count);
        Assert.Equal(2UL, streamer.PacketsSent);
        Assert.Equal((ulong)(2 * transport.Sent[0].Length), streamer.BytesSent);
        Assert.True(UdpPacketCodec.TryDecode(transport.Sent[0], out var seq0, out _, out var s0));
        Assert.True(UdpPacketCodec.TryDecode(transport.Sent[1], out var seq1, out _, out _));
        Assert.Equal(0u, seq0);
        Assert.Equal(1u, seq1);
        Assert.All(s0, v => Assert.Equal(0.25f, v, 5));
    }

    [Fact]
    public void BlockSent_event_carries_rendered_samples()
    {
        var engine = new SignalEngine(48_000);
        engine.UpdateChannels(new[] { new ChannelConfig { Waveform = WaveformKind.Dc, Amplitude = 0.5 } });
        using var streamer = new SignalStreamer(engine, new FakeTransport());
        float[]? seen = null;
        streamer.BlockSent += s => seen = s.ToArray();
        streamer.SendOneBlock();
        Assert.NotNull(seen);
        Assert.Equal(streamer.BlockSize, seen!.Length);
    }

    [Fact]
    public void BlockSize_defaults_to_20ms_at_engine_rate()
    {
        var streamer = new SignalStreamer(new SignalEngine(48_000), new FakeTransport());
        Assert.Equal(960, streamer.BlockSize);
    }

    [Fact]
    public void Timer_fire_catches_up_missed_blocks_with_consecutive_seq()
    {
        long period = (long)Math.Round(960.0 * Stopwatch.Frequency / 48_000); // same math as the streamer
        var time = new ManualTimeProvider();
        var transport = new FakeTransport();
        using var streamer = new SignalStreamer(new SignalEngine(48_000), transport, time);

        streamer.Start();
        time.Advance(3 * period);
        Assert.NotNull(time.Fire);
        time.Fire!();

        Assert.Equal(3, transport.Sent.Count); // 3 periods late, overdue = 2: no resync, all three blocks sent
        for (uint i = 0; i < transport.Sent.Count; i++)
        {
            Assert.True(UdpPacketCodec.TryDecode(transport.Sent[(int)i], out var seq, out _, out _));
            Assert.Equal(i, seq); // catch-up keeps seq consecutive: 0, 1, 2
        }
    }

    [Fact]
    public void Timer_fire_after_long_gap_resyncs_and_sends_one_block()
    {
        long period = (long)Math.Round(960.0 * Stopwatch.Frequency / 48_000); // same math as the streamer
        var time = new ManualTimeProvider();
        var transport = new FakeTransport();
        using var streamer = new SignalStreamer(new SignalEngine(48_000), transport, time);

        streamer.Start();
        time.Advance(10 * period);
        Assert.NotNull(time.Fire);
        time.Fire!();

        Assert.Single(transport.Sent); // 9 periods overdue: backlog dropped, phase re-locked to now, one fresh block
    }

    [Fact]
    public void Fire_after_Stop_does_not_send()
    {
        long period = (long)Math.Round(960.0 * Stopwatch.Frequency / 48_000); // same math as the streamer
        var time = new ManualTimeProvider();
        var transport = new FakeTransport();
        using var streamer = new SignalStreamer(new SignalEngine(48_000), transport, time);

        streamer.Start();
        time.Advance(period); // a block is due: without the _timer-is-null drop guard this late callback would send it
        streamer.Stop();
        Assert.NotNull(time.Fire);
        time.Fire!(); // late callback: dispatched before Dispose, acquires the lock only after Stop returned

        Assert.Empty(transport.Sent);
        Assert.Equal(0UL, streamer.PacketsSent);
    }
}
