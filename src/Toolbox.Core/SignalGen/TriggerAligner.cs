namespace Toolbox.Core.SignalGen;

/// <summary>Scope-style trigger: finds a stable rising edge so the preview appears stationary.</summary>
public static class TriggerAligner
{
    /// <summary>Width of the hysteresis band as a fraction of the signal half-swing
    /// (a quarter of the half-swing sits on each side of the midpoint).</summary>
    private const float HysteresisFraction = 0.25f;

    /// <summary>
    /// Finds the index of the first sample that rises through the upper edge of a hysteresis band
    /// centered on the signal midpoint (Schmitt-trigger style). The trigger arms while the signal
    /// sits at or below mid - h and fires at the first sample at or above mid + h, where
    /// mid = (min + max) / 2 and h = HysteresisFraction * (max - min) / 2. Noise riding near the
    /// trigger level cannot cross the whole band in one step, so spurious early triggers are
    /// rejected — the classic oscilloscope fix for a display that jitters horizontally when a
    /// noise channel is superimposed on the signal.
    /// </summary>
    /// <param name="samples">Input samples to scan.</param>
    /// <returns>Index of the first sample that fires after arming; 0 when the span is empty,
    /// flat, or never fires (caller paints from the start).</returns>
    public static int FindRisingEdge(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return 0;

        float min = samples[0];
        float max = samples[0];
        foreach (float s in samples)
        {
            if (s < min) min = s;
            if (s > max) max = s;
        }

        // Flat signal: max - min is effectively zero, so the band has zero width and any
        // sample would "cross" it — there is no edge to trigger on.
        if (max - min <= float.Epsilon) return 0;

        float mid = (min + max) / 2f;
        float h = HysteresisFraction * ((max - min) / 2f);

        bool armed = false;
        for (int i = 0; i < samples.Length; i++)
        {
            if (!armed)
                armed = samples[i] <= mid - h;
            else if (samples[i] >= mid + h)
                return i;
        }

        return 0;
    }
}
