namespace Toolbox.Core;

/// <summary>
/// Example pure-logic service shared by the tools: counts clicks.
/// </summary>
public class ClickCounter
{
    public int Count { get; private set; }

    public int Increment()
    {
        Count++;
        return Count;
    }

    public void Reset() => Count = 0;
}
