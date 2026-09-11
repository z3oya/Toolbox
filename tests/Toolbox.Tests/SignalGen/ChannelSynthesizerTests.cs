namespace Toolbox.Tests.SignalGen;

using Toolbox.Core.SignalGen;

public class ChannelSynthesizerTests
{
    private const int Fs = 48_000;
    private static double Dt => 1.0 / Fs;

    private static double[] Render(ChannelSynthesizer synth, int n)
    {
        var y = new double[n];
        for (int i = 0; i < n; i++) y[i] = synth.NextSample(Dt);
        return y;
    }

    [Fact]
    public void Sine_matches_closed_form()
    {
        var synth = new ChannelSynthesizer(new ChannelConfig
        {
            Waveform = WaveformKind.Sine, Frequency = 1000, Amplitude = 0.8,
        });
        var y = Render(synth, 10);
        for (int i = 0; i < 10; i++)
            Assert.Equal(0.8 * Math.Sin(WaveformMath.TwoPi * 1000 * i * Dt), y[i], 9);
    }

    [Fact]
    public void Disabled_channel_outputs_zero()
    {
        var synth = new ChannelSynthesizer(new ChannelConfig { Enabled = false, Amplitude = 1.0 });
        Assert.All(Render(synth, 5), v => Assert.Equal(0.0, v, 12));
    }

    [Fact]
    public void Seeded_noise_is_reproducible_and_bounded()
    {
        var a = new ChannelSynthesizer(new ChannelConfig { Waveform = WaveformKind.Noise, Amplitude = 1.0 }, noiseSeed: 42);
        var b = new ChannelSynthesizer(new ChannelConfig { Waveform = WaveformKind.Noise, Amplitude = 1.0 }, noiseSeed: 42);
        var ya = Render(a, 100);
        var yb = Render(b, 100);
        Assert.Equal(ya, yb);
        Assert.All(ya, v => Assert.True(v is > -1.0 and <= 1.0));
    }

    [Fact]
    public void Am_envelope_follows_1_plus_depth_times_modulator()
    {
        // Carrier DC (amplitude 1) + AM sine modulation: the output directly exposes the envelope.
        var synth = new ChannelSynthesizer(new ChannelConfig
        {
            Waveform = WaveformKind.Dc, Amplitude = 1.0,
            Mod = new ModulationConfig { Kind = ModulationKind.Am, ModFrequency = 100, Depth = 0.5 },
        });
        var y = Render(synth, Fs / 100); // one modulation period
        for (int i = 0; i < y.Length; i++)
        {
            double expect = 1.0 + 0.5 * Math.Sin(WaveformMath.TwoPi * 100 * i * Dt);
            Assert.Equal(expect, y[i], 9);
        }
    }

    [Fact]
    public void Fm_shifts_instantaneous_frequency()
    {
        // DC carrier (phase does not affect the output, but CarrierPhase still advances at the
        // instantaneous frequency) + FM square modulation of ±100 Hz.
        var synth = new ChannelSynthesizer(new ChannelConfig
        {
            Waveform = WaveformKind.Dc, Frequency = 1000,
            Mod = new ModulationConfig { Kind = ModulationKind.Fm, ModWave = WaveformKind.Square, ModFrequency = 10, Depth = 100.0 },
        });
        double Wrap(double d) { d %= WaveformMath.TwoPi; return d < 0 ? d + WaveformMath.TwoPi : d; }
        double Rate(int n)
        {
            double before = synth.CarrierPhase;
            for (int i = 0; i < n; i++) synth.NextSample(Dt);
            return Wrap(synth.CarrierPhase - before);
        }

        double r1 = Rate(48);   // t≈0, first modulator half (square = +1) → f = 1100 Hz
        // Advance into the MIDDLE of the second half instead of stopping right at the i=2400
        // transition: the modulator phase accumulated over 2400 steps lands ~1e-13 BELOW π
        // (accumulation rounding), so a window starting exactly at the edge would see one extra
        // +1 sample — an unstable knife edge. Mid-half is ~1200 samples from both transitions.
        for (int i = 0; i < 3600 - 48; i++) synth.NextSample(Dt);
        double r2 = Rate(48);   // mid second half (square = -1) → f = 900 Hz

        Assert.Equal(Wrap(WaveformMath.TwoPi * 1100 * 48 * Dt), r1, 6);
        Assert.Equal(Wrap(WaveformMath.TwoPi * 900 * 48 * Dt), r2, 6);
    }

    [Fact]
    public void ApplyConfig_keeps_phase_running_without_reset()
    {
        var synth = new ChannelSynthesizer(new ChannelConfig { Frequency = 100 });
        for (int i = 0; i < 480; i++) synth.NextSample(Dt);
        double before = synth.CarrierPhase;
        synth.ApplyConfig(new ChannelConfig { Frequency = 200 });
        synth.NextSample(Dt);
        double delta = synth.CarrierPhase - before;
        if (delta < 0) delta += WaveformMath.TwoPi;
        Assert.Equal(WaveformMath.TwoPi * 200 * Dt, delta, 9); // phase continues at the new frequency, no reset
    }

    [Fact]
    public void Phase_offset_is_applied_at_read_time_and_wraps_past_2pi()
    {
        // A nonzero phase offset on a non-sine waveform: carrier phase + offset crosses 2π
        // mid-render (at i=12 of 24), so a synthesizer that forgot to wrap the sum would feed
        // WaveformMath.Value out-of-range phase and Triangle would leave [-1, 1].
        var synth = new ChannelSynthesizer(new ChannelConfig
        {
            Waveform = WaveformKind.Triangle, Frequency = 1000, Amplitude = 1.0, Phase = 1.5 * Math.PI,
        });
        static double Triangle(double phase) => phase < Math.PI ? -1.0 + 2.0 * phase / Math.PI : 3.0 - 2.0 * phase / Math.PI;
        double Wrap(double d) { d %= WaveformMath.TwoPi; return d < 0 ? d + WaveformMath.TwoPi : d; }
        var y = Render(synth, 24);
        for (int i = 0; i < 24; i++)
        {
            double total = Wrap(WaveformMath.TwoPi * 1000 * i * Dt + 1.5 * Math.PI);
            Assert.Equal(Triangle(total), y[i], 9);
        }
    }

    [Fact]
    public void Disabling_outputs_silence_and_freezes_carrier_phase()
    {
        ChannelConfig Config(bool enabled) => new() { Waveform = WaveformKind.Sine, Frequency = 1000, Amplitude = 1.0, Enabled = enabled };
        var synth = new ChannelSynthesizer(Config(true));
        for (int i = 0; i < 100; i++) synth.NextSample(Dt); // carrier phase ≈ π/6 after 100 samples
        double beforeFreeze = synth.CarrierPhase;

        synth.ApplyConfig(Config(false));
        Assert.All(Render(synth, 50), v => Assert.Equal(0.0, v, 12));
        Assert.Equal(beforeFreeze, synth.CarrierPhase); // silenced samples advanced nothing

        synth.ApplyConfig(Config(true));
        double y = synth.NextSample(Dt); // reads the frozen phase, then advances from it
        Assert.True(Math.Abs(y) > 0.1);  // output actually resumes (sin(π/6) ≈ 0.5)
        Assert.Equal(Math.Sin(beforeFreeze), y, 12);
        double delta = synth.CarrierPhase - beforeFreeze;
        if (delta < 0) delta += WaveformMath.TwoPi;
        Assert.Equal(WaveformMath.TwoPi * 1000 * Dt, delta, 12); // first advance continues from the frozen value
    }

    [Fact]
    public void Disabling_freezes_modulator_and_envelope_resumes_mid_cycle()
    {
        ChannelConfig EnabledConfig() => new()
        {
            Waveform = WaveformKind.Dc, Amplitude = 1.0,
            Mod = new ModulationConfig { Kind = ModulationKind.Am, ModFrequency = 100, Depth = 0.5 },
        };
        var synth = new ChannelSynthesizer(EnabledConfig());
        for (int i = 0; i < 120; i++) synth.NextSample(Dt); // into mid-cycle (i=120 of the 480-sample period; sin = 1)

        synth.ApplyConfig(EnabledConfig() with { Enabled = false });
        Assert.All(Render(synth, 200), v => Assert.Equal(0.0, v, 12));

        synth.ApplyConfig(EnabledConfig());
        // The modulator phase advanced only by the post-reenable samples: the envelope continues
        // at sample index 120 (1.5), not as if restarted at sin(0) (1.0).
        for (int i = 0; i < 10; i++)
            Assert.Equal(1.0 + 0.5 * Math.Sin(WaveformMath.TwoPi * 100 * (120 + i) * Dt), synth.NextSample(Dt), 9);
    }

    [Fact]
    public void ApplyConfig_mod_depth_change_keeps_modulator_phase_running()
    {
        ChannelConfig Config(double depth) => new()
        {
            Waveform = WaveformKind.Dc, Amplitude = 1.0,
            Mod = new ModulationConfig { Kind = ModulationKind.Am, ModFrequency = 100, Depth = depth },
        };
        var synth = new ChannelSynthesizer(Config(0.5));
        for (int i = 0; i < 120; i++) synth.NextSample(Dt); // modulator phase ≈ π/2 (sin = 1)

        synth.ApplyConfig(Config(0.25)); // same ModFrequency: only the depth changes
        // Envelope continues from the running modulator phase (1.25 at i=120 with the new depth),
        // not from a restarted sin(0) (which would give 1.0).
        for (int i = 0; i < 10; i++)
            Assert.Equal(1.0 + 0.25 * Math.Sin(WaveformMath.TwoPi * 100 * (120 + i) * Dt), synth.NextSample(Dt), 9);
    }

    [Fact]
    public void Am_depth_one_envelope_bottoms_out_at_exactly_zero()
    {
        var synth = new ChannelSynthesizer(new ChannelConfig
        {
            Waveform = WaveformKind.Dc, Amplitude = 1.0,
            Mod = new ModulationConfig { Kind = ModulationKind.Am, ModFrequency = 100, Depth = 1.0 },
        });
        var y = Render(synth, 480); // one full modulation period
        Assert.All(y, v => Assert.True(v >= 0.0)); // 1 + 1·sin never goes negative
        Assert.Equal(0.0, y[360]);  // trough at modulator phase 3π/2: 1 + 1·(-1) = 0 exactly
    }

    [Fact]
    public void Fm_with_square_carrier_composes_duty_and_phase_offset()
    {
        // FM square modulator (±300 Hz) on a duty-0.25 square carrier with a phase offset:
        // within the first modulator half the instantaneous frequency is constant (1300 Hz),
        // so the carrier argument has an exact closed form including the CarrierArgument wrap.
        var synth = new ChannelSynthesizer(new ChannelConfig
        {
            Waveform = WaveformKind.Square, Frequency = 1000, Amplitude = 0.5,
            DutyCycle = 0.25, Phase = 1.5 * Math.PI,
            Mod = new ModulationConfig { Kind = ModulationKind.Fm, ModWave = WaveformKind.Square, ModFrequency = 10, Depth = 300.0 },
        });
        double Wrap(double d) { d %= WaveformMath.TwoPi; return d < 0 ? d + WaveformMath.TwoPi : d; }
        var y = Render(synth, 100); // entirely inside the first modulator half (square = +1)
        for (int i = 0; i < 100; i++)
        {
            double phase = Wrap(WaveformMath.TwoPi * 1300 * i * Dt + 1.5 * Math.PI);
            double expect = phase < WaveformMath.TwoPi * 0.25 ? 0.5 : -0.5;
            Assert.Equal(expect, y[i]);
        }
    }
}
