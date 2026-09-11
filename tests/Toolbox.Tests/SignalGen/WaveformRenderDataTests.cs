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
    public void MinMax_with_fewer_samples_than_buckets_spreads_proportionally()
    {
        // Unified floor tiling: start = floor(L*b/B), end = floor(L*(b+1)/B); an empty range
        // (start == end) holds samples[start]. The trace stretches across the full width with
        // step-holds at proportional pixel positions instead of a tail pinned to the last
        // sample, which jumps around frame-to-frame under noise.
        var two = WaveformDownsampler.MinMax(new float[] { 0.25f, -0.25f }, 5);
        // L=2, B=5: starts 0,0,0,1,1; ends 0,0,1,1,2 -> buckets 0-2 from sample 0, buckets 3-4 from sample 1.
        Assert.Equal((0.25f, 0.25f), two[0]);
        Assert.Equal((0.25f, 0.25f), two[1]);
        Assert.Equal((0.25f, 0.25f), two[2]);
        Assert.Equal((-0.25f, -0.25f), two[3]);
        Assert.Equal((-0.25f, -0.25f), two[4]);

        var three = WaveformDownsampler.MinMax(new float[] { 0f, -1f, 0.5f }, 5);
        // L=3, B=5: ranges [0,0), [0,1), [1,1), [1,2), [2,3); empty ranges hold samples[start].
        Assert.Equal((0f, 0f), three[0]);
        Assert.Equal((0f, 0f), three[1]);
        Assert.Equal((-1f, -1f), three[2]);
        Assert.Equal((-1f, -1f), three[3]);
        Assert.Equal((0.5f, 0.5f), three[4]);
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
    public void FindRisingEdge_returns_first_upward_band_crossing()
    {
        var samples = new float[] { 0.5f, -0.2f, -0.1f, 0.3f, -0.4f, 0.1f };
        Assert.Equal(3, TriggerAligner.FindRisingEdge(samples));
    }

    [Fact]
    public void FindRisingEdge_with_start_at_phase_zero_triggers()
    {
        // mid = 0.05, h = 0.0125: the first sample (a sine starting at phase 0) sits at or
        // below the arm level (0 <= 0.0375) and the second clears the fire level
        // (0.1 >= 0.0625), so the trigger fires on the first rising step.
        Assert.Equal(1, TriggerAligner.FindRisingEdge(new float[] { 0f, 0.1f }));
    }

    [Fact]
    public void FindRisingEdge_returns_zero_when_none()
    {
        // Descending ramp: it only arms at the last sample (0.1 <= 0.4), so there is no
        // rise left to fire on. (The pre-hysteresis case {1f, 0.5f, 1f} now legitimately
        // triggers at 2 — a dip below mid-band followed by a recovery IS a rising edge;
        // see Dc_offset_signal_triggers_at_mid_band.)
        Assert.Equal(0, TriggerAligner.FindRisingEdge(new float[] { 0.9f, 0.5f, 0.1f }));
        Assert.Equal(0, TriggerAligner.FindRisingEdge(Array.Empty<float>()));
    }

    [Fact]
    public void Noise_wiggle_near_zero_does_not_trigger_early()
    {
        // min=-0.5, max=0.9 -> mid=0.2, h=0.175. The -0.02/+0.03 wiggle near indices 2-3
        // stays inside the hysteresis band, so the trigger waits for the genuine rise to
        // 0.8. The old first-zero-crossing scan returned 3 (the spurious wiggle crossing),
        // which made the display jitter horizontally whenever a noise channel was added.
        var samples = new float[] { 0.9f, 0.1f, -0.02f, 0.03f, -0.5f, -0.4f, 0.2f, 0.8f, 0.9f };
        Assert.Equal(7, TriggerAligner.FindRisingEdge(samples));
    }

    [Fact]
    public void Dc_offset_signal_triggers_at_mid_band()
    {
        // Never crosses absolute zero, yet the scope must still lock onto the edge:
        // min=0.2, max=1.0 -> mid=0.6, h=0.1; arms at 0.4 (<= 0.5), fires at 0.8 (>= 0.7).
        // The old zero-crossing scan returned 0 (no crossing at all).
        var samples = new float[] { 0.6f, 0.8f, 1.0f, 0.8f, 0.6f, 0.4f, 0.2f, 0.4f, 0.6f, 0.8f, 1.0f };
        Assert.Equal(9, TriggerAligner.FindRisingEdge(samples));

        // Same mid-band semantics on a short dip: mid=0.75, h=0.0625; arms in the dip
        // (0.5 <= 0.6875), fires on the recovery (1.0 >= 0.8125).
        Assert.Equal(2, TriggerAligner.FindRisingEdge(new float[] { 1f, 0.5f, 1f }));
    }

    [Fact]
    public void Flat_signal_returns_zero()
    {
        // max - min is zero: no swing, no band, no trigger; caller paints from the start.
        Assert.Equal(0, TriggerAligner.FindRisingEdge(new float[] { 0.5f, 0.5f, 0.5f }));
    }

    [Fact]
    public void Noise_added_to_tone_does_not_shift_trigger_before_the_band()
    {
        // 40 samples: a triangle tone (peak +0.90 at i=0, trough -0.90 at i=20) with
        // deterministic +/-0.15 wiggles superimposed on the descent near its zero level:
        //
        //   i=8..12 clean: +0.18  +0.09   0.00  -0.09  -0.18
        //          noise:  -0.15  -0.15  +0.15  +0.15  +0.15
        //       composite: +0.03  -0.06  +0.15  +0.06  -0.03
        //
        // The -0.06 -> +0.15 hop at i=9..10 is a spurious rising "crossing": the old
        // first-zero-crossing scan returned 10, shifting the display a quarter cycle early.
        //
        // min=-0.90, max=+0.90 -> mid=0.00, h=0.225. The clean edge rises +0.09 per
        // sample (0.18 at i=32, 0.27 at i=33), so it first clears mid+h=0.225 at i=33;
        // the noise wiggles never leave the band, so the trigger must fire at 33.
        var samples = new float[]
        {
            // clean descent: 0.90 - 0.09*i (i = 0..7)
            0.90f, 0.81f, 0.72f, 0.63f, 0.54f, 0.45f, 0.36f, 0.27f,
            // descent near zero with +/-0.15 wiggles (i = 8..12)
            0.03f, -0.06f, 0.15f, 0.06f, -0.03f,
            // clean descent continues (i = 13..19)
            -0.27f, -0.36f, -0.45f, -0.54f, -0.63f, -0.72f, -0.81f,
            // trough (i = 20), then clean rising edge: -0.90 + 0.09*(i-20) (i = 21..39)
            -0.90f,
            -0.81f, -0.72f, -0.63f, -0.54f, -0.45f, -0.36f, -0.27f, -0.18f, -0.09f, 0.00f,
            0.09f, 0.18f, 0.27f, 0.36f, 0.45f, 0.54f, 0.63f, 0.72f, 0.81f,
        };
        Assert.Equal(40, samples.Length);
        Assert.Equal(33, TriggerAligner.FindRisingEdge(samples));
    }
}
