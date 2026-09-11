namespace Toolbox.Core.SignalGen;

/// <summary>Immutable per-channel settings. Output = Σ enabled channels, clamped to ±1.</summary>
public sealed record ChannelConfig
{
    public string Name { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public WaveformKind Waveform { get; init; } = WaveformKind.Sine;
    public double Frequency { get; init; } = 1000.0;   // Hz
    public double Amplitude { get; init; } = 0.5;      // 0..1, normalized full scale
    public double Phase { get; init; }                 // rad; Clamped() wraps into [0, 2π) so the value feeds WaveformMath.Value directly
    public double DutyCycle { get; init; } = 0.5;      // square wave only, 0.01..0.99
    public ModulationConfig? Mod { get; init; }        // null = clean carrier

    /// <summary>Returns a copy with all fields clamped to their valid ranges. Non-finite inputs (NaN, ±∞) are sanitized to the field's lower bound (phase to 0).</summary>
    public ChannelConfig Clamped()
    {
        var frequency = Sanitize(Frequency, 0.1, 20_000.0);
        var phase = !double.IsFinite(Phase) ? 0.0 : Phase % WaveformMath.TwoPi; // ∞ % 2π = NaN, so the gate must be finiteness, not just NaN
        if (phase < 0) phase += WaveformMath.TwoPi;
        if (phase >= WaveformMath.TwoPi) phase -= WaveformMath.TwoPi; // a tiny negative phase can round the += up to exactly 2π; restore the half-open [0, 2π) invariant
        ModulationConfig? mod = Mod?.Clamped();
        // FM deviation must not drive the instantaneous frequency negative; cap it against the clamped carrier.
        if (mod is { Kind: ModulationKind.Fm } m) mod = m with { Depth = Math.Min(m.Depth, frequency) };
        return this with
        {
            Frequency = frequency,
            Amplitude = Sanitize(Amplitude, 0.0, 1.0),
            Phase = phase,
            DutyCycle = Sanitize(DutyCycle, 0.01, 0.99),
            Mod = mod,
        };
    }

    // NaN flows through Math.Clamp unchanged and would poison the synthesizer; map it to the lower bound instead.
    private static double Sanitize(double v, double min, double max) => double.IsNaN(v) ? min : Math.Clamp(v, min, max);
}
