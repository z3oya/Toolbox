namespace Toolbox.Tests.SignalGen;

using Toolbox.Core.SignalGen;

public class WaveformMathTests
{
    private const double TwoPi = 2.0 * Math.PI;

    [Theory]
    [InlineData(0.0, 0.0)]            // phase 0 -> 0
    [InlineData(Math.PI / 2, 1.0)]    // peak
    [InlineData(Math.PI, 0.0)]        // zero crossing (approximate)
    public void Sine_returns_expected(double phase, double expected)
        => Assert.Equal(expected, WaveformMath.Value(WaveformKind.Sine, phase), 6);

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(0.49 * TwoPi, 1.0)]   // within 50% duty cycle -> high
    [InlineData(0.51 * TwoPi, -1.0)]  // outside -> low
    public void Square_respects_duty_cycle(double phase, double expected)
        => Assert.Equal(expected, WaveformMath.Value(WaveformKind.Square, phase, 0.5), 6);

    [Fact]
    public void Square_at_exact_half_period_is_low()
    {
        // Strict '<' semantics: a phase exactly on the duty boundary counts as low.
        var phase = WaveformMath.TwoPi / 2.0;

        Assert.Equal(-1.0, WaveformMath.Value(WaveformKind.Square, phase, 0.5), 9);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.75 * TwoPi)]
    public void Square_duty_zero_is_always_low(double phase)
        => Assert.Equal(-1.0, WaveformMath.Value(WaveformKind.Square, phase, 0.0), 6);

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.75 * TwoPi)]
    public void Square_duty_one_is_always_high(double phase)
        => Assert.Equal(1.0, WaveformMath.Value(WaveformKind.Square, phase, 1.0), 6);

    [Theory]
    [InlineData(0.0, -1.0)]
    [InlineData(0.25 * TwoPi, 0.0)]
    [InlineData(0.5 * TwoPi, 1.0)]    // triangle peak
    [InlineData(0.75 * TwoPi, 0.0)]
    public void Triangle_peaks_at_half_period(double phase, double expected)
        => Assert.Equal(expected, WaveformMath.Value(WaveformKind.Triangle, phase), 6);

    [Theory]
    [InlineData(0.0, -1.0)]
    [InlineData(0.5 * TwoPi, 0.0)]
    public void Sawtooth_rises_from_minus1(double phase, double expected)
        => Assert.Equal(expected, WaveformMath.Value(WaveformKind.Sawtooth, phase), 6);

    [Fact]
    public void Dc_returns_constant_one_regardless_of_phase()
        => Assert.Equal(1.0, WaveformMath.Value(WaveformKind.Dc, 1.234), 9);

    [Fact]
    public void Noise_returns_fixed_zero_by_contract()
        => Assert.Equal(0.0, WaveformMath.Value(WaveformKind.Noise, 0.0), 9); // noise samples come from the stateful synthesizer, never from here

    [Fact]
    public void Value_throws_for_undefined_kind()
        => Assert.Throws<ArgumentOutOfRangeException>(() => WaveformMath.Value((WaveformKind)999, 0.0));
}
