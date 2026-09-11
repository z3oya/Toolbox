namespace Toolbox.Core.SignalGen;

/// <summary>Min/max bucket downsampling: one bucket per pixel column keeps peaks visible at any zoom.</summary>
public static class WaveformDownsampler
{
    /// <summary>Reduces the samples to <paramref name="bucketCount"/> min/max pairs, one per pixel column.</summary>
    /// <param name="samples">Input samples; empty input yields all-zero buckets (paints a flat line at zero). Non-finite samples are deliberately not validated (hot path); the caller owns validation — the engine clamps its output to [-1, 1].</param>
    /// <param name="bucketCount">Number of buckets to produce, typically the pixel width of the target view. Must be positive.</param>
    /// <returns>One (min, max) pair per bucket; with fewer samples than buckets the extra buckets hold the last sample so the trace still spans the full width.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bucketCount"/> is zero or negative.</exception>
    public static (float Min, float Max)[] MinMax(ReadOnlySpan<float> samples, int bucketCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bucketCount);
        var buckets = new (float Min, float Max)[bucketCount];
        if (samples.Length == 0) return buckets;

        if (samples.Length <= bucketCount) // fewer samples than buckets: hold last value
        {
            for (int b = 0; b < bucketCount; b++)
            {
                int idx = Math.Min(b, samples.Length - 1);
                buckets[b] = (samples[idx], samples[idx]);
            }
            return buckets;
        }

        for (int b = 0; b < bucketCount; b++)
        {
            int start = (int)((long)samples.Length * b / bucketCount);
            int end = (int)((long)samples.Length * (b + 1) / bucketCount);
            float min = samples[start], max = samples[start];
            for (int i = start + 1; i < end; i++)
            {
                if (samples[i] < min) min = samples[i];
                if (samples[i] > max) max = samples[i];
            }
            buckets[b] = (min, max);
        }
        return buckets;
    }
}
