namespace Toolbox.Tests.SignalGen;

using Toolbox.Core.SignalGen;

public class ChannelConfigTests
{
    [Fact]
    public void Clamp_limits_frequency_amplitude_and_duty()
    {
        var cfg = new ChannelConfig { Frequency = 1e9, Amplitude = 42.0, DutyCycle = 3.0 };
        var c = cfg.Clamped();
        Assert.Equal(20_000.0, c.Frequency);
        Assert.Equal(1.0, c.Amplitude);
        Assert.Equal(0.99, c.DutyCycle, 9);
    }

    [Fact]
    public void Clamp_wraps_phase_into_0_to_2pi()
    {
        var c = new ChannelConfig { Phase = -Math.PI / 2 }.Clamped();
        Assert.InRange(c.Phase, 0.0, WaveformMath.TwoPi);
        Assert.Equal(3.0 * Math.PI / 2, c.Phase, 9);
    }

    [Fact]
    public void Clamp_limits_am_depth_to_0_1_and_mod_frequency()
    {
        var m = new ModulationConfig { Kind = ModulationKind.Am, Depth = 5.0, ModFrequency = 1e6 }.Clamped();
        Assert.Equal(1.0, m.Depth);
        Assert.Equal(20_000.0, m.ModFrequency);
    }

    [Fact]
    public void Clamp_limits_fm_deviation_to_carrier_frequency()
    {
        // FM deviation must not drive the instantaneous frequency negative.
        var c = new ChannelConfig
        {
            Frequency = 100.0,
            Mod = new ModulationConfig { Kind = ModulationKind.Fm, Depth = 500.0 },
        }.Clamped();
        Assert.Equal(100.0, c.Mod!.Depth); // deviation <= carrier -> minimum instantaneous frequency >= 0
    }

    [Fact]
    public void Clamp_preserves_noise_waveform_and_still_caps_frequency()
    {
        var c = new ChannelConfig { Waveform = WaveformKind.Noise, Frequency = 1e9 }.Clamped();
        Assert.Equal(WaveformKind.Noise, c.Waveform);
        Assert.Equal(20_000.0, c.Frequency); // frequency is meaningless for noise but the uniform cap still applies
    }

    [Fact]
    public void Clamp_preserves_null_modulation()
    {
        var c = new ChannelConfig { Mod = null }.Clamped();
        Assert.Null(c.Mod);
    }

    [Fact]
    public void Fm_depth_below_carrier_is_untouched()
    {
        var c = new ChannelConfig
        {
            Frequency = 1000.0,
            Mod = new ModulationConfig { Kind = ModulationKind.Fm, Depth = 50.0 },
        }.Clamped();
        Assert.Equal(50.0, c.Mod!.Depth, 9);
    }

    [Fact]
    public void Clamped_is_idempotent()
    {
        var cfg = new ChannelConfig
        {
            Frequency = 1e9,
            Phase = -Math.PI / 2,
            DutyCycle = 3.0,
            Mod = new ModulationConfig { Kind = ModulationKind.Fm, Depth = 500.0 },
        };

        Assert.Equal(cfg.Clamped(), cfg.Clamped().Clamped());
    }

    [Fact]
    public void Clamp_enforces_lower_bounds()
    {
        var c = new ChannelConfig { Frequency = -5.0, Amplitude = -1.0, DutyCycle = 0.0 }.Clamped();
        Assert.Equal(0.1, c.Frequency, 9);
        Assert.Equal(0.0, c.Amplitude, 9);
        Assert.Equal(0.01, c.DutyCycle, 9);
    }

    [Fact]
    public void Clamp_wraps_positive_phase_into_0_to_2pi()
    {
        var c = new ChannelConfig { Phase = 3.0 * Math.PI }.Clamped();
        Assert.Equal(Math.PI, c.Phase, 9);
    }

    [Fact]
    public void Clamp_tiny_negative_phase_wraps_to_exactly_zero_not_2pi()
    {
        // (-1e-300 % 2pi) + 2pi rounds up to exactly 2pi without the half-open guard; the invariant is [0, 2pi).
        var c = new ChannelConfig { Phase = -1e-300 }.Clamped();
        Assert.Equal(0.0, c.Phase);
        Assert.True(c.Phase >= 0.0 && c.Phase < WaveformMath.TwoPi);
    }

    [Fact]
    public void Clamp_caps_fm_deviation_at_clamped_carrier_not_raw_frequency()
    {
        // A raw negative carrier must not leak a negative deviation past the FM cap.
        var c = new ChannelConfig
        {
            Frequency = -50.0,
            Mod = new ModulationConfig { Kind = ModulationKind.Fm, Depth = 200.0 },
        }.Clamped();
        Assert.True(c.Mod!.Depth >= 0.0);
        Assert.Equal(0.1, c.Mod.Depth, 9); // deviation is capped by the clamped carrier (0.1), never negative
    }

    [Fact]
    public void Clamp_sanitizes_infinite_phase_to_zero()
    {
        // ±Infinity % 2π = NaN, which a NaN-only check would let through; the gate must be finiteness.
        var c = new ChannelConfig { Phase = double.PositiveInfinity }.Clamped();
        Assert.Equal(0.0, c.Phase);
        c = new ChannelConfig { Phase = double.NegativeInfinity }.Clamped();
        Assert.Equal(0.0, c.Phase);
    }

    [Fact]
    public void Clamp_sanitizes_nan_inputs_to_safe_values()
    {
        var c = new ChannelConfig
        {
            Frequency = double.NaN,
            Amplitude = double.NaN,
            Phase = double.NaN,
            DutyCycle = double.NaN,
            Mod = new ModulationConfig { Kind = ModulationKind.Am, Depth = double.NaN, ModFrequency = double.NaN },
        }.Clamped();
        Assert.Equal(0.1, c.Frequency, 9);
        Assert.Equal(0.0, c.Amplitude, 9);
        Assert.Equal(0.0, c.Phase, 9);
        Assert.Equal(0.01, c.DutyCycle, 9);
        Assert.Equal(0.0, c.Mod!.Depth, 9);
        Assert.Equal(0.01, c.Mod.ModFrequency, 9);
    }
}
