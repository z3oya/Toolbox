namespace Toolbox.Tests.SignalGen;

using Toolbox.Core.SignalGen;

public class PhaseAccumulatorTests
{
    [Fact]
    public void Advance_accumulates_and_wraps_into_0_2pi()
    {
        var acc = new PhaseAccumulator();
        acc.Advance(0.75 * WaveformMath.TwoPi);
        acc.Advance(0.75 * WaveformMath.TwoPi);
        Assert.Equal(0.5 * WaveformMath.TwoPi, acc.Phase, 9);
    }

    [Fact]
    public void Advance_handles_negative_deltas()
    {
        var acc = new PhaseAccumulator();
        acc.Advance(-0.25 * WaveformMath.TwoPi);
        Assert.True(acc.Phase >= 0.0 && acc.Phase < WaveformMath.TwoPi, $"phase {acc.Phase} not in half-open [0, 2pi)");
        Assert.Equal(0.75 * WaveformMath.TwoPi, acc.Phase, 9);
    }

    [Fact]
    public void Reset_restores_initial_phase()
    {
        var acc = new PhaseAccumulator();
        acc.Advance(1.0);
        acc.Reset(0.5);
        Assert.Equal(0.5, acc.Phase, 9);
    }

    [Fact]
    public void Wrap_is_value_preserving() // waveform value is unchanged after advancing by whole periods
    {
        var acc = new PhaseAccumulator();
        double sinBefore = Math.Sin(acc.Phase);
        acc.Advance(WaveformMath.TwoPi * 123.0);
        Assert.True(acc.Phase >= 0.0 && acc.Phase < WaveformMath.TwoPi, $"phase {acc.Phase} not in half-open [0, 2pi)");
        Assert.Equal(sinBefore, Math.Sin(acc.Phase), 12);
    }

    [Fact]
    public void Reset_default_returns_phase_to_exactly_zero()
    {
        var acc = new PhaseAccumulator();
        acc.Advance(1.0);
        acc.Reset();
        Assert.Equal(0.0, acc.Phase);
    }

    [Fact]
    public void Reset_full_turn_wraps_to_exactly_zero()
    {
        var acc = new PhaseAccumulator();
        acc.Reset(WaveformMath.TwoPi);
        Assert.Equal(0.0, acc.Phase);
    }

    [Fact]
    public void Reset_three_pi_wraps_to_pi()
    {
        var acc = new PhaseAccumulator();
        acc.Reset(3.0 * Math.PI);
        Assert.Equal(Math.PI, acc.Phase, 9);
    }

    [Fact]
    public void Reset_tiny_negative_phase_stays_strictly_below_2pi()
    {
        // (-1e-300 % 2pi) + 2pi rounds up to exactly 2pi without the half-open guard; the invariant is [0, 2pi).
        var acc = new PhaseAccumulator();
        acc.Reset(-1e-300);
        Assert.Equal(0.0, acc.Phase);
        Assert.True(acc.Phase < WaveformMath.TwoPi);
    }
}
