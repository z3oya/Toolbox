namespace Toolbox.Core.SignalGen;

/// <summary>Multi-channel summing engine. Render fills a buffer with the clamped (±1) sum of all
/// enabled channels. UpdateChannels swaps configs under a lock; per-channel phases survive updates
/// (channels are matched by index: leading synths are kept, added indices get fresh synths, trailing
/// ones are dropped). Thread-safe: UpdateChannels (UI thread), Render (sending thread) and
/// DebugCarrierPhase (preview on UI thread) serialize on one gate. Render holds the lock for the
/// whole buffer (typically a few ms of audio) — intentional: simple and correct, and UpdateChannels
/// callers run at UI rate so the trade is fine.</summary>
public sealed class SignalEngine
{
    private readonly object _gate = new();
    private ChannelSynthesizer[] _synths = Array.Empty<ChannelSynthesizer>();

    public int SampleRate { get; }

    public SignalEngine(int sampleRate = 48_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate); // dt = 1/SampleRate must stay finite; SignalStreamer (Task 7) divides by this too
        SampleRate = sampleRate;
    }

    public void UpdateChannels(IReadOnlyList<ChannelConfig> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        int count = channels.Count; // snapshot once: guards against a caller mutating the list mid-update
        lock (_gate)
        {
            if (_synths.Length != count)
            {
                var old = _synths;
                _synths = new ChannelSynthesizer[count];
                for (int i = 0; i < count; i++)
                    _synths[i] = i < old.Length ? old[i] : new ChannelSynthesizer(channels[i]);
            }
            for (int i = 0; i < count; i++)
                _synths[i].ApplyConfig(channels[i]);
        }
    }

    public void Render(Span<float> buffer)
    {
        double dt = 1.0 / SampleRate;
        lock (_gate)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                double sum = 0.0;
                foreach (var synth in _synths) sum += synth.NextSample(dt);
                buffer[i] = (float)Math.Clamp(sum, -1.0, 1.0);
            }
        }
    }

    /// <summary>Test hook: carrier phase of channel <paramref name="index"/> (for continuity assertions).</summary>
    public double DebugCarrierPhase(int index)
    {
        lock (_gate) return _synths[index].CarrierPhase;
    }
}
