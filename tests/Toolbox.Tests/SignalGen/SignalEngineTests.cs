namespace Toolbox.Tests.SignalGen;

using Toolbox.Core.SignalGen;

public class SignalEngineTests
{
    private const int Fs = 48_000;

    [Theory]
    [InlineData(0)]
    [InlineData(-48000)]
    public void Ctor_rejects_non_positive_sample_rate(int badFs) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SignalEngine(badFs));

    [Fact]
    public void Render_sums_enabled_channels()
    {
        var engine = new SignalEngine(Fs);
        engine.UpdateChannels(new[]
        {
            new ChannelConfig { Waveform = WaveformKind.Dc, Amplitude = 0.25 },
            new ChannelConfig { Waveform = WaveformKind.Dc, Amplitude = 0.50 },
            new ChannelConfig { Waveform = WaveformKind.Dc, Amplitude = 0.10, Enabled = false },
        });
        var buf = new float[4];
        engine.Render(buf);
        Assert.All(buf, s => Assert.Equal(0.75f, s, 6));
    }

    [Fact]
    public void Render_hard_clamps_to_pm_one()
    {
        var engine = new SignalEngine(Fs);
        engine.UpdateChannels(Enumerable.Repeat(
            new ChannelConfig { Waveform = WaveformKind.Dc, Amplitude = 1.0 }, 4).ToArray());
        var buf = new float[8];
        engine.Render(buf);
        Assert.All(buf, s => Assert.Equal(1.0f, s, 6));
    }

    [Fact]
    public void UpdateChannels_growing_list_keeps_existing_phases()
    {
        var engine = new SignalEngine(Fs);
        engine.UpdateChannels(new[] { new ChannelConfig { Frequency = 100 } });
        var buf = new float[10];
        engine.Render(buf);
        double phaseBefore = engine.DebugCarrierPhase(0);
        engine.UpdateChannels(new[]
        {
            new ChannelConfig { Frequency = 100 },
            new ChannelConfig { Frequency = 500 }, // added channel
        });
        Assert.Equal(phaseBefore, engine.DebugCarrierPhase(0), 12); // channel 0 phase not reset
    }

    [Fact]
    public void UpdateChannels_replaces_config_atomically()
    {
        var engine = new SignalEngine(Fs);
        engine.UpdateChannels(new[] { new ChannelConfig { Waveform = WaveformKind.Dc, Amplitude = 0.5 } });
        engine.UpdateChannels(new[] { new ChannelConfig { Waveform = WaveformKind.Dc, Amplitude = 0.1 } });
        var buf = new float[2];
        engine.Render(buf);
        Assert.All(buf, s => Assert.Equal(0.1f, s, 6)); // new config takes effect immediately
    }

    [Fact]
    public void Render_before_any_channels_outputs_silence()
    {
        var engine = new SignalEngine(Fs);
        var buf = new float[16];
        engine.Render(buf);
        Assert.All(buf, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void UpdateChannels_shrinking_list_keeps_leading_phases()
    {
        var engine = new SignalEngine(Fs);
        engine.UpdateChannels(new[]
        {
            new ChannelConfig { Frequency = 100 },
            new ChannelConfig { Frequency = 200 },
            new ChannelConfig { Frequency = 300 },
        });
        var buf = new float[8];
        engine.Render(buf);
        double p0 = engine.DebugCarrierPhase(0);
        engine.UpdateChannels(new[] { new ChannelConfig { Frequency = 100 } });
        Assert.Equal(p0, engine.DebugCarrierPhase(0), 12);
        // re-grow: channel 1 is NEW (fresh synth) — verify via phase reset to 0
        engine.UpdateChannels(new[]
        {
            new ChannelConfig { Frequency = 100 },
            new ChannelConfig { Frequency = 200 },
        });
        Assert.Equal(0.0, engine.DebugCarrierPhase(1), 12);
    }

    [Fact]
    public async Task Render_concurrent_with_UpdateChannels_is_safe()
    {
        // Grow/shrink swap pattern concurrent with Render: the single _gate lock must
        // keep every sample in [-1,1] and never throw (no torn array access).
        var engine = new SignalEngine(48_000);
        engine.UpdateChannels(new[] { new ChannelConfig { Waveform = WaveformKind.Dc, Amplitude = 0.1 } });
        var buf = new float[480];
        var stop = new ManualResetEventSlim(false);
        var updater = Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 2_000; i++)
                    engine.UpdateChannels(i % 2 == 0
                        ? new[] { new ChannelConfig { Waveform = WaveformKind.Dc, Amplitude = 0.1 } }
                        : new[] { new ChannelConfig { Waveform = WaveformKind.Dc, Amplitude = 0.1 },
                                  new ChannelConfig { Waveform = WaveformKind.Dc, Amplitude = 0.2 } });
            }
            finally { stop.Set(); } // the main thread spins on stop: it must be set even if UpdateChannels throws
        });
        int renders = 0;
        while (!stop.IsSet)
        {
            engine.Render(buf);
            Assert.All(buf, s => Assert.True(s is >= -1f and <= 1f));
            renders++;
        }
        Assert.True(renders > 0);
        await updater.WaitAsync(TimeSpan.FromSeconds(10)); // rethrows an updater failure with its stack, unlike an opaque IsCompletedSuccessfully assert
    }
}
