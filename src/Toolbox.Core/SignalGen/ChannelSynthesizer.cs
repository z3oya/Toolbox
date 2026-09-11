namespace Toolbox.Core.SignalGen;

/// <summary>Stateful per-channel synthesizer: carrier oscillator + optional AM/FM modulator.
/// Config changes take effect on the next sample; phases keep running (no glitch).
/// Disabled channels output silence and freeze all state (carrier, modulator, noise);
/// re-enabling resumes from the frozen phase.</summary>
public sealed class ChannelSynthesizer
{
    private readonly PhaseAccumulator _carrier = new();
    private readonly PhaseAccumulator _modulator = new();
    private readonly Random _noise;
    private ChannelConfig _config;

    public ChannelSynthesizer(ChannelConfig initial, int? noiseSeed = null)
    {
        _config = initial.Clamped();
        _noise = noiseSeed.HasValue ? new Random(noiseSeed.Value) : Random.Shared;
    }

    /// <summary>Current carrier phase (rad). Exposed for continuity tests.</summary>
    public double CarrierPhase => _carrier.Phase;

    public void ApplyConfig(ChannelConfig config) => _config = config.Clamped();

    /// <summary>Produces the next output sample for the current configuration.</summary>
    /// <param name="dt">Sample period in seconds (e.g. 1/48000). Must be finite and positive;
    /// deliberately not validated (hot path) — the caller owns validation.</param>
    public double NextSample(double dt)
    {
        var cfg = _config;
        if (!cfg.Enabled) return 0.0; // disabled channels advance no state (modulator frozen too)

        double mod = 0.0;
        var m = cfg.Mod;
        if (m != null)
        {
            mod = m.ModWave == WaveformKind.Noise
                ? NextNoise()
                : WaveformMath.Value(m.ModWave, _modulator.Phase);
            _modulator.Advance(WaveformMath.TwoPi * m.ModFrequency * dt);
        }

        double freq = cfg.Frequency;
        if (m?.Kind == ModulationKind.Fm) freq += m.Depth * mod; // Hz deviation, capped at the carrier by Clamped()

        double y = cfg.Waveform == WaveformKind.Noise
            ? NextNoise()
            : WaveformMath.Value(cfg.Waveform, CarrierArgument(cfg), cfg.DutyCycle);
        _carrier.Advance(WaveformMath.TwoPi * freq * dt);

        double amp = cfg.Amplitude;
        if (m?.Kind == ModulationKind.Am) amp *= 1.0 + m.Depth * mod;
        return y * amp;
    }

    // Accumulator phase + user phase offset, wrapped back into the [0, 2π) range WaveformMath.Value
    // expects. Both operands are in [0, 2π), so the sum is in [0, 4π) and one conditional subtract
    // suffices. The offset is applied at read time (not baked into the accumulator), so ApplyConfig
    // phase changes are instant while the accumulator keeps running glitch-free.
    private double CarrierArgument(ChannelConfig cfg)
    {
        double p = _carrier.Phase + cfg.Phase;
        return p >= WaveformMath.TwoPi ? p - WaveformMath.TwoPi : p;
    }

    private double NextNoise() => 2.0 * _noise.NextDouble() - 1.0;
}
