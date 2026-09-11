namespace Toolbox.Core.SignalGen;

/// <summary>Stateless waveform value functions over phase [0, 2π). Noise is handled by the caller (stateful).</summary>
public static class WaveformMath
{
    public const double TwoPi = 2.0 * Math.PI;

    /// <summary>Computes the value of the given waveform at the specified phase.</summary>
    /// <param name="kind">Waveform shape to evaluate.</param>
    /// <param name="phase">Phase in radians, expected pre-wrapped to [0, 2π); behavior outside that range is undefined.</param>
    /// <param name="dutyCycle">Fraction of the period the square wave spends high, expected in [0, 1]. Deliberately not validated (hot path); the caller owns validation.</param>
    /// <returns>Waveform value in [-1, 1]; Dc returns a fixed 1.0 (before amplitude scaling) and Noise a fixed 0.0.</returns>
    public static double Value(WaveformKind kind, double phase, double dutyCycle = 0.5) => kind switch
    {
        WaveformKind.Sine => Math.Sin(phase),
        WaveformKind.Square => phase < TwoPi * dutyCycle ? 1.0 : -1.0,
        WaveformKind.Triangle => phase < Math.PI
            ? -1.0 + 2.0 * phase / Math.PI
            : 3.0 - 2.0 * phase / Math.PI,
        WaveformKind.Sawtooth => 2.0 * phase / TwoPi - 1.0,
        WaveformKind.Dc => 1.0,
        WaveformKind.Noise => 0.0, // never called; synthesizers use their own RNG
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
