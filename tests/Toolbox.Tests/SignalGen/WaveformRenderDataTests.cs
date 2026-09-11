namespace Toolbox.Tests.SignalGen;

using Toolbox.Core.SignalGen;

public class WaveformRenderDataTests
{
    [Fact]
    public void MinMax_buckets_reduce_samples_per_pixel()
    {
        // 4 samples -> 2 buckets: min/max of each half
        var buckets = WaveformDownsampler.MinMax(new float[] { 0f, -1f, 0.5f, 1f }, 2);
        Assert.Equal((-1f, 0f), buckets[0]);
        Assert.Equal((0.5f, 1f), buckets[1]);
    }

    [Fact]
    public void MinMax_with_fewer_samples_than_buckets_repeats_last()
    {
        var buckets = WaveformDownsampler.MinMax(new float[] { 0.25f, -0.25f }, 5);
        Assert.Equal((0.25f, 0.25f), buckets[0]);
        Assert.Equal((-0.25f, -0.25f), buckets[1]);
        Assert.Equal((-0.25f, -0.25f), buckets[4]); // spare buckets hold the last sample
    }

    [Fact]
    public void MinMax_empty_input_returns_zero_buckets()
    {
        var buckets = WaveformDownsampler.MinMax(Array.Empty<float>(), 3);
        Assert.Equal(3, buckets.Length);
        Assert.All(buckets, b => Assert.Equal((0f, 0f), b));
    }

    [Fact]
    public void MinMax_single_sample_fills_all()
    {
        var buckets = WaveformDownsampler.MinMax(new float[] { 0.7f }, 4);
        Assert.All(buckets, b => Assert.Equal((0.7f, 0.7f), b));
    }

    [Fact]
    public void MinMax_non_divisible_spread_uses_floor_boundaries()
    {
        // L=5, B=2: floor-division tiling gives bucket 0 = [0,2) (2 samples) and bucket 1 = [2,5) (3 samples);
        // the earlier bucket takes the floor share, never the rounded-up one.
        var buckets = WaveformDownsampler.MinMax(new float[] { 0f, -1f, 0.5f, 0.2f, 1f }, 2);
        Assert.Equal((-1f, 0f), buckets[0]);
        Assert.Equal((0.2f, 1f), buckets[1]);
    }

    [Fact]
    public void FindRisingZeroCross_returns_first_upward_crossing()
    {
        var samples = new float[] { 0.5f, -0.2f, -0.1f, 0.3f, -0.4f, 0.1f };
        Assert.Equal(3, TriggerAligner.FindRisingZeroCross(samples));
    }

    [Fact]
    public void FindRisingZeroCross_with_prev_exactly_zero_triggers()
    {
        // '<=' semantics: a sample exactly at zero counts as non-positive, so a sine starting
        // at phase 0 triggers on its first rising step.
        Assert.Equal(1, TriggerAligner.FindRisingZeroCross(new float[] { 0f, 0.1f }));
    }

    [Fact]
    public void FindRisingZeroCross_returns_zero_when_none()
    {
        Assert.Equal(0, TriggerAligner.FindRisingZeroCross(new float[] { 1f, 0.5f, 1f }));
        Assert.Equal(0, TriggerAligner.FindRisingZeroCross(Array.Empty<float>()));
    }
}
