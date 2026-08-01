using DarkAmbientRadio.Core.Config;

namespace DarkAmbientRadio.Core.Tests;

public class WindowPlacementTests
{
    // A single 1920x1080 monitor at the origin.
    private static bool OnSingleScreen(WindowPlacement p) => p.IsOnScreen(0, 0, 1920, 1080);

    private static WindowPlacement At(double left, double top, double w = 1040, double h = 640)
        => new() { Left = left, Top = top, Width = w, Height = h };

    [Fact]
    public void IsOnScreen_AcceptsAWindowFullyInside()
        => Assert.True(OnSingleScreen(At(100, 100)));

    [Fact]
    public void IsOnScreen_AcceptsAWindowHangingOffTheEdge()
    {
        Assert.True(OnSingleScreen(At(1800, 100)));    // most of it past the right edge
        Assert.True(OnSingleScreen(At(-900, 100)));    // most of it past the left edge
    }

    [Fact]
    public void IsOnScreen_RejectsAWindowOnADetachedMonitor()
    {
        Assert.False(OnSingleScreen(At(3000, 100)));   // second monitor to the right, now gone
        Assert.False(OnSingleScreen(At(-2000, 100)));  // second monitor to the left, now gone
    }

    [Fact]
    public void IsOnScreen_RejectsATitleBarAboveOrBelowTheDesktop()
    {
        Assert.False(OnSingleScreen(At(100, -50)));    // dragged above: unreachable title bar
        Assert.False(OnSingleScreen(At(100, 1050)));   // below the taskbar
    }

    [Theory]
    [InlineData(0, 640)]
    [InlineData(1040, 0)]
    [InlineData(-10, -10)]
    public void IsOnScreen_RejectsDegenerateSizes(double width, double height)
        => Assert.False(OnSingleScreen(At(100, 100, width, height)));

    [Fact]
    public void IsOnScreen_HandlesAMonitorLeftOfThePrimary()
    {
        // Virtual desktop spanning two screens, origin shifted into the negative.
        var placement = At(-1500, 100);
        Assert.True(placement.IsOnScreen(-1920, 0, 3840, 1080));
    }
}
