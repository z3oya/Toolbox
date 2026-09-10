namespace Toolbox.Core;

/// <summary>
/// Example pure-logic service shared by the tools: counts clicks.
/// </summary>
public class ClickCounter
{
    public int Count { get; private set; }

    /// <summary>True when the count is even (used to toggle UI state).</summary>
    public bool IsEven => Count % 2 == 0;

    public int Increment()
    {
        Count++;
        return Count;
    }

    public void Reset() => Count = 0;
}
