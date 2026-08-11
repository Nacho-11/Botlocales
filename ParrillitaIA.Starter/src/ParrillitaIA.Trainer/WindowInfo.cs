using System.Text;

namespace ParrillitaIA.Trainer;

internal static class WindowInfo
{
    public static WindowSnapshot GetForeground()
    {
        var handle = NativeMethods.GetForegroundWindow();
        if (handle == IntPtr.Zero)
            return WindowSnapshot.Empty;

        var title = new StringBuilder(1024);
        NativeMethods.GetWindowText(handle, title, title.Capacity);

        var className = new StringBuilder(512);
        NativeMethods.GetClassName(handle, className, className.Capacity);

        if (!NativeMethods.GetWindowRect(handle, out var rect))
            return WindowSnapshot.Empty;

        return new WindowSnapshot(
            handle,
            title.ToString(),
            className.ToString(),
            rect.Left,
            rect.Top,
            rect.Width,
            rect.Height);
    }

    public static IntPtr FindBestWindow(string title, string className)
    {
        IntPtr best = IntPtr.Zero;
        var bestScore = -1;

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true;

            var currentTitle = new StringBuilder(1024);
            NativeMethods.GetWindowText(hWnd, currentTitle, currentTitle.Capacity);

            var currentClass = new StringBuilder(512);
            NativeMethods.GetClassName(hWnd, currentClass, currentClass.Capacity);

            var score = 0;

            if (!string.IsNullOrWhiteSpace(title))
            {
                if (string.Equals(
                        currentTitle.ToString(),
                        title,
                        StringComparison.OrdinalIgnoreCase))
                    score += 100;
                else if (currentTitle.ToString().Contains(
                             title,
                             StringComparison.OrdinalIgnoreCase) ||
                         title.Contains(
                             currentTitle.ToString(),
                             StringComparison.OrdinalIgnoreCase))
                    score += 50;
            }

            if (!string.IsNullOrWhiteSpace(className) &&
                string.Equals(
                    currentClass.ToString(),
                    className,
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 25;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = hWnd;
            }

            return true;
        }, IntPtr.Zero);

        return bestScore > 0 ? best : IntPtr.Zero;
    }
}

internal readonly record struct WindowSnapshot(
    IntPtr Handle,
    string Title,
    string ClassName,
    int Left,
    int Top,
    int Width,
    int Height)
{
    public static WindowSnapshot Empty =>
        new(IntPtr.Zero, string.Empty, string.Empty, 0, 0, 0, 0);
}
