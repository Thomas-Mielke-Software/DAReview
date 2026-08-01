namespace DarkAmbientRadio.Core.Config;

/// <summary>
/// Persisted main-window geometry. Stored in device-independent WPF units (not pixels), so it
/// survives a DPI change. The size is always the <em>restored</em> size — a maximised window
/// records where it would go when un-maximised, which is what the user expects on restart.
/// </summary>
public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }

    /// <summary>
    /// Guards against restoring onto a monitor that is no longer attached (docking station
    /// unplugged, projector removed) — the window would be invisible and unreachable.
    /// </summary>
    public bool IsOnScreen(double virtualLeft, double virtualTop, double virtualWidth, double virtualHeight)
    {
        if (Width <= 0 || Height <= 0)
            return false;

        // Require a reasonable chunk of the title bar area to be inside the virtual desktop.
        const double MinVisible = 80;
        var right = virtualLeft + virtualWidth;
        var bottom = virtualTop + virtualHeight;

        return Left + Width - MinVisible > virtualLeft
               && Left + MinVisible < right
               && Top + MinVisible < bottom
               && Top >= virtualTop - 1;
    }
}
