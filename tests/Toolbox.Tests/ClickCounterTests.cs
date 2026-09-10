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

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(10, true)]
    public void IsEven_reflects_count_parity(int increments, bool expectedEven)
    {
        var counter = new ClickCounter();

        for (var i = 0; i < increments; i++)
        {
            counter.Increment();
        }

        Assert.Equal(expectedEven, counter.IsEven);
    }

    [Fact]
    public void Reset_clears_count()
    {
        var counter = new ClickCounter();
        counter.Increment();
        counter.Increment();

        counter.Reset();

        Assert.Equal(0, counter.Count);
        Assert.True(counter.IsEven);
    }
}
