using System.ComponentModel;
using Toolbox.Core.SignalGen;

namespace Toolbox.Ui.WinForms.SignalGen;

/// <summary>
/// Oscilloscope-style waveform display: min/max buckets per pixel column,
/// optional rising-edge trigger alignment, grid, auto/manual vertical scale.
/// Triggered rendering draws a fixed sweep (exactly one block of samples) locked to the
/// trigger over a rolling two-block buffer, so the horizontal scale never moves with the
/// trigger index; the display lags the signal by one block. Sample data hand-off is
/// thread-safe (reference swap); call <see cref="SetSamples"/> on the UI thread so
/// invalidation is legal.
/// </summary>
public class WaveformView : Control
{
    private float[] _samples = Array.Empty<float>();
    private float[]? _previousSamples;                   // block before _samples (view-owned, no copy)
    private float[] _triggerSpan = Array.Empty<float>(); // previous + current, for fixed-sweep triggering
    private float _fullScale = 1f;

    // Browsable(false) + Hidden serialization: these are configured in code
    // at runtime, not via a designer (required to satisfy analyzer WFO1000).
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Triggered { get; set; } = true;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AutoScale { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color WaveColor { get; set; } = Color.FromArgb(0, 200, 160);

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color GridColor { get; set; } = Color.FromArgb(35, 35, 45);

    public WaveformView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(16, 16, 22);
    }

    /// <summary>Thread-safe data hand-off: copies samples, atomically swaps the buffer reference, and stashes the replaced block into a rolling two-block trigger span. Call on the UI thread so invalidation is legal.</summary>
    public void SetSamples(ReadOnlySpan<float> samples)
    {
        var copy = samples.ToArray();
        float peak = 1e-6f;
        foreach (var s in copy) peak = Math.Max(peak, Math.Abs(s));
        _fullScale = AutoScale ? peak : 1f;

        // Rolling two-block buffer: stash the block being replaced (already view-owned, so
        // no extra copy), then concatenate previous + current so a trigger firing late in
        // the current block still has a full sweep of samples after it.
        var previous = _samples;
        var triggerSpan = new float[previous.Length + copy.Length];
        previous.AsSpan().CopyTo(triggerSpan);
        copy.AsSpan().CopyTo(triggerSpan.AsSpan(previous.Length));
        System.Threading.Volatile.Write(ref _previousSamples, previous);
        System.Threading.Volatile.Write(ref _triggerSpan, triggerSpan);
        System.Threading.Interlocked.Exchange(ref _samples, copy);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        int w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height), mid = h / 2;

        using (var grid = new Pen(GridColor))
        {
            g.DrawLine(grid, 0, mid, w, mid);
            for (int x = w / 10; x < w; x += Math.Max(1, w / 10)) g.DrawLine(grid, x, 0, x, h);
            for (int y = mid - h / 4; y > 0; y -= Math.Max(1, h / 4)) g.DrawLine(grid, 0, y, w, y);
            for (int y = mid + h / 4; y < h; y += Math.Max(1, h / 4)) g.DrawLine(grid, 0, y, w, y);
        }

        var samples = System.Threading.Volatile.Read(ref _samples);
        if (samples.Length == 0) return;
        ReadOnlySpan<float> view = samples;
        if (Triggered)
        {
            // Fixed sweep: render exactly one block (the sweep length) starting at the trigger,
            // scanning the rolling two-block span with the search constrained so at least
            // `sweep` samples follow the firing index — the horizontal scale never moves with
            // the trigger index. Falls back to single-block alignment when there is no previous
            // block yet or no edge fires inside the constrained window (one transitional frame).
            int sweep = samples.Length;
            var previous = System.Threading.Volatile.Read(ref _previousSamples);
            var triggerSpan = System.Threading.Volatile.Read(ref _triggerSpan);
            int idx = 0;
            if (previous is not null && triggerSpan.Length >= sweep) {
                idx = TriggerAligner.FindRisingEdge(triggerSpan.AsSpan(0, triggerSpan.Length - sweep + 1));
            }

            if (idx > 0) {
                view = triggerSpan.AsSpan(idx, sweep);
            } else {
                idx = TriggerAligner.FindRisingEdge(view);
                if (idx > 0) view = view[idx..];
            }
        }

        var buckets = WaveformDownsampler.MinMax(view, w);
        float scale = (h / 2f) * 0.92f / _fullScale;
        using var wave = new Pen(WaveColor);
        for (int x = 0; x < buckets.Length; x++)
        {
            float yTop = mid - buckets[x].Max * scale;
            float yBot = mid - buckets[x].Min * scale;
            g.DrawLine(wave, x, yTop, x, Math.Max(yBot, yTop + 0.5f));
        }
    }
}
