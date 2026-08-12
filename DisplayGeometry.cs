using System.Windows;
using System.Windows.Interop;
using FormsScreen = System.Windows.Forms.Screen;
using DrawingRectangle = System.Drawing.Rectangle;

namespace GuguPet;

internal enum DisplayEdge
{
    Left,
    Right,
    Top,
    Bottom
}

internal sealed record DisplayArea(
    string DeviceName,
    Rect WorkArea,
    IReadOnlyList<DisplayEdge> ExposedEdges);

internal static class DisplayGeometry
{
    public static DisplayArea ForWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var screen = handle != IntPtr.Zero
            ? FormsScreen.FromHandle(handle)
            : FormsScreen.PrimaryScreen ?? FormsScreen.AllScreens[0];

        var workArea = ToWpfRect(window, screen.WorkingArea);
        return new DisplayArea(screen.DeviceName, workArea, GetExposedEdges(screen));
    }

    public static Rect ClampRect(Rect area, double width, double height, double left, double top)
    {
        var maxLeft = Math.Max(area.Left, area.Right - Math.Max(0, width));
        var maxTop = Math.Max(area.Top, area.Bottom - Math.Max(0, height));
        return new Rect(
            Math.Clamp(left, area.Left, maxLeft),
            Math.Clamp(top, area.Top, maxTop),
            Math.Min(Math.Max(0, width), area.Width),
            Math.Min(Math.Max(0, height), area.Height));
    }

    private static Rect ToWpfRect(Window window, DrawingRectangle pixels)
    {
        if (PresentationSource.FromVisual(window) is not null &&
            double.IsFinite(window.Left) && double.IsFinite(window.Top))
        {
            try
            {
                // PointFromScreen handles the monitor's active DPI. Adding the
                // window position converts the local result back to WPF's
                // desktop coordinate space without assuming a global scale.
                var localTopLeft = window.PointFromScreen(new System.Windows.Point(pixels.Left, pixels.Top));
                var localBottomRight = window.PointFromScreen(new System.Windows.Point(pixels.Right, pixels.Bottom));
                return new Rect(
                    window.Left + localTopLeft.X,
                    window.Top + localTopLeft.Y,
                    Math.Max(0, localBottomRight.X - localTopLeft.X),
                    Math.Max(0, localBottomRight.Y - localTopLeft.Y));
            }
            catch (InvalidOperationException)
            {
                // The visual may be between handles during startup/shutdown.
            }
        }

        return SystemParameters.WorkArea;
    }

    private static IReadOnlyList<DisplayEdge> GetExposedEdges(FormsScreen screen)
    {
        var bounds = screen.Bounds;
        var exposed = new List<DisplayEdge>();

        if (!FormsScreen.AllScreens.Any(other =>
                other.DeviceName != screen.DeviceName &&
                Touches(other.Bounds.Right, bounds.Left) &&
                Overlaps(other.Bounds.Top, other.Bounds.Bottom, bounds.Top, bounds.Bottom)))
            exposed.Add(DisplayEdge.Left);

        if (!FormsScreen.AllScreens.Any(other =>
                other.DeviceName != screen.DeviceName &&
                Touches(other.Bounds.Left, bounds.Right) &&
                Overlaps(other.Bounds.Top, other.Bounds.Bottom, bounds.Top, bounds.Bottom)))
            exposed.Add(DisplayEdge.Right);

        if (!FormsScreen.AllScreens.Any(other =>
                other.DeviceName != screen.DeviceName &&
                Touches(other.Bounds.Bottom, bounds.Top) &&
                Overlaps(other.Bounds.Left, other.Bounds.Right, bounds.Left, bounds.Right)))
            exposed.Add(DisplayEdge.Top);

        if (!FormsScreen.AllScreens.Any(other =>
                other.DeviceName != screen.DeviceName &&
                Touches(other.Bounds.Top, bounds.Bottom) &&
                Overlaps(other.Bounds.Left, other.Bounds.Right, bounds.Left, bounds.Right)))
            exposed.Add(DisplayEdge.Bottom);

        return exposed;
    }

    private static bool Touches(int first, int second) => Math.Abs(first - second) <= 1;

    private static bool Overlaps(int firstStart, int firstEnd, int secondStart, int secondEnd) =>
        Math.Max(firstStart, secondStart) < Math.Min(firstEnd, secondEnd);
}
