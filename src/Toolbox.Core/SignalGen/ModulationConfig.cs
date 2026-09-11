namespace Toolbox.Core.SignalGen;

/// <summary>Modulator settings. For AM, Depth is envelope depth in [0,1]; for FM, Depth is peak deviation in Hz.</summary>
public sealed record ModulationConfig
{
    public ModulationKind Kind { get; init; } = ModulationKind.Am;
    public WaveformKind ModWave { get; init; } = WaveformKind.Sine;
    public double ModFrequency { get; init; } = 10.0;
    public double Depth { get; init; } = 0.5;

    /// <summary>Returns a copy with all fields clamped to their valid ranges. NaN inputs are sanitized to the field's lower bound.</summary>
    public ModulationConfig Clamped() => this with
    {
        ModFrequency = Sanitize(ModFrequency, 0.01, 20_000.0),
        // AM depth is a [0,1] ratio; FM depth is a deviation in Hz, capped by the carrier in ChannelConfig.
        Depth = Kind == ModulationKind.Am
            ? Sanitize(Depth, 0.0, 1.0)
            : Sanitize(Depth, 0.0, 20_000.0),
    };

    // NaN flows through Math.Clamp unchanged and would poison the synthesizer; map it to the lower bound instead.
    private static double Sanitize(double v, double min, double max) => double.IsNaN(v) ? min : Math.Clamp(v, min, max);
}
