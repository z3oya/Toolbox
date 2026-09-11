namespace Toolbox.Core.SignalGen;

/// <summary>Scope-style trigger: finds the first rising zero crossing so the preview appears stationary.</summary>
public static class TriggerAligner
{
    /// <summary>Finds the index of the first sample that crosses from non-positive into strictly positive territory.</summary>
    /// <param name="samples">Input samples to scan.</param>
    /// <returns>Index of the first sample where the previous sample is &lt;= 0 and the current sample is &gt; 0; 0 when no such crossing exists (caller paints from the start).</returns>
    public static int FindRisingZeroCross(ReadOnlySpan<float> samples)
    {
        for (int i = 1; i < samples.Length; i++)
            if (samples[i - 1] <= 0f && samples[i] > 0f) return i;
        return 0;
    }
}
