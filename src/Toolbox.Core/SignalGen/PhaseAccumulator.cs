namespace Toolbox.Core.SignalGen;

/// <summary>Continuous phase accumulator; wrapping is value-preserving so parameter changes never glitch. Not thread-safe: single-threaded per engine.</summary>
public sealed class PhaseAccumulator
{
    private double _phase;

    /// <summary>Current phase in radians, always within the half-open interval [0, 2π).</summary>
    public double Phase => _phase;

    /// <summary>Advances the phase by the given increment, wrapping into [0, 2π).</summary>
    /// <param name="deltaPhase">Phase increment in radians; may be negative or larger than 2π. Must be finite; NaN/∞ behavior is deliberately not validated (hot path) — the caller owns validation.</param>
    public void Advance(double deltaPhase)
    {
        _phase += deltaPhase;
        _phase %= WaveformMath.TwoPi;
        if (_phase < 0) _phase += WaveformMath.TwoPi;
        if (_phase >= WaveformMath.TwoPi) _phase -= WaveformMath.TwoPi; // a tiny negative remainder can round the += up to exactly 2π; restore the half-open [0, 2π) invariant
    }

    /// <summary>Resets the phase to the given value, wrapped into [0, 2π).</summary>
    /// <param name="phase">Phase in radians; may be negative or larger than 2π. Must be finite; NaN/∞ behavior is deliberately not validated (hot path) — the caller owns validation.</param>
    public void Reset(double phase = 0.0)
    {
        _phase = phase % WaveformMath.TwoPi;
        if (_phase < 0) _phase += WaveformMath.TwoPi;
        if (_phase >= WaveformMath.TwoPi) _phase -= WaveformMath.TwoPi; // a tiny negative input can round the += up to exactly 2π; restore the half-open [0, 2π) invariant
    }
}
