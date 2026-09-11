namespace Toolbox.Core.SignalGen;

/// <summary>Min/max bucket downsampling: one bucket per pixel column keeps peaks visible at any zoom.</summary>
public static class WaveformDownsampler
{
    /// <summary>Reduces the samples to <paramref name="bucketCount"/> min/max pairs, one per pixel column.</summary>
    /// <param name="samples">Input samples; empty input yields all-zero buckets (paints a flat line at zero). Non-finite samples are deliberately not validated (hot path); the caller owns validation — the engine clamps its output to [-1, 1].</param>
    /// <param name="bucketCount">Number of buckets to produce, typically the pixel width of the target view. Must be positive.</param>
    /// <returns>One (min, max) pair per bucket, tiled proportionally: bucket b covers the sample range
    /// [floor(L*b/B), floor(L*(b+1)/B)). With more samples than buckets this decimates the signal into
    /// per-column min/max pairs; with fewer samples than buckets the empty ranges hold the sample at
    /// their start position, so the trace stretches across the full width with step-holds. A held-last-sample
    /// tail is deliberately avoided: it is positionally unstable under noise (the tail jumps with the random
    /// last sample), and a 1:1 sample-to-pixel mapping changes the effective timebase whenever the trigger
    /// index moves between frames.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bucketCount"/> is zero or negative.</exception>
    public static (float Min, float Max)[] MinMax(ReadOnlySpan<float> samples, int bucketCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bucketCount);
        var buckets = new (float Min, float Max)[bucketCount];
        if (samples.Length == 0) return buckets;

        // One proportional floor-tiling loop for every length: an empty range (start == end,
        // only possible when samples < buckets) holds samples[start], so values step at
        // proportional pixel positions and the trace always spans the full width.
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
