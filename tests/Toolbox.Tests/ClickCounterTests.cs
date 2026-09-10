using Toolbox.Core;

namespace Toolbox.Tests;

public class ClickCounterTests
{
    [Fact]
    public void Increment_returns_running_count()
    {
        var counter = new ClickCounter();

        Assert.Equal(1, counter.Increment());
        Assert.Equal(2, counter.Increment());
        Assert.Equal(2, counter.Count);
    }

    [Fact]
    public void Reset_clears_count()
    {
        var counter = new ClickCounter();
        counter.Increment();
        counter.Increment();

        counter.Reset();

        Assert.Equal(0, counter.Count);
    }
}
