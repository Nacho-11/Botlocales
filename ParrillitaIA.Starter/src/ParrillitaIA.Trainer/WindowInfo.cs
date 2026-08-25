using System.Diagnostics;
using System.Text;

namespace ParrillitaIA.Trainer;

internal readonly record struct WindowSnapshot(
    IntPtr Handle,
    string ProcessName,
    string Title,
    string ClassName,
    int Left,
    int Top,
    int Width,
    int Height);

internal static class WindowInfo
{
    public static WindowSnapshot GetForeground() =>
        GetSnapshot(NativeMethods.GetForegroundWindow());

    public static WindowSnapshot GetSnapshot(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return default;

        var title = new StringBuilder(1024);
        var className = new StringBuilder(512);

        NativeMethods.GetWindowText(
            handle,
            title,
            title.Capacity);

        NativeMethods.GetClassName(
            handle,
            className,
            className.Capacity);

        if (!NativeMethods.GetWindowRect(
                handle,
                out var rect))
        {
            return default;
        }

        NativeMethods.GetWindowThreadProcessId(
            handle,
            out var processId);

        var processName = string.Empty;

        try
        {
            processName = Process
                .GetProcessById((int)processId)
                .ProcessName;
        }
        catch
        {
        }

        return new WindowSnapshot(
            handle,
            processName,
            title.ToString(),
            className.ToString(),
            rect.Left,
            rect.Top,
            rect.Width,
            rect.Height);
    }

    public static IntPtr FindBestWindow(WorkflowStep step)
    {
        // Primero intentar con la ventana activa.
        var foreground = GetForeground();

        if (foreground.Handle != IntPtr.Zero &&
            string.Equals(
                foreground.ProcessName,
                step.ProcessName,
                StringComparison.OrdinalIgnoreCase))
        {
            return foreground.Handle;
        }

        IntPtr best = IntPtr.Zero;
        var bestScore = int.MinValue;

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle))
                return true;

            var current = GetSnapshot(handle);

            if (current.Handle == IntPtr.Zero)
                return true;

            // La ventana debe pertenecer al mismo proceso.
            if (!string.Equals(
                    current.ProcessName,
                    step.ProcessName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var score = 100;

            // Título estable.
            if (!string.IsNullOrWhiteSpace(step.StableTitle) &&
                current.Title.Contains(
                    step.StableTitle,
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 50;
            }

            // Clase.
            if (!string.IsNullOrWhiteSpace(step.WindowClass) &&
                string.Equals(
                    step.WindowClass,
                    current.ClassName,
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 30;
            }

            // Tamaño aproximado.
            if (step.RecordedWindowWidth > 0 &&
                step.RecordedWindowHeight > 0 &&
                current.Width > 0 &&
                current.Height > 0)
            {
                var widthDelta =
                    Math.Abs(
                        current.Width -
                        step.RecordedWindowWidth) /
                    (double)step.RecordedWindowWidth;

                var heightDelta =
                    Math.Abs(
                        current.Height -
                        step.RecordedWindowHeight) /
                    (double)step.RecordedWindowHeight;

                if (widthDelta <= 0.10 &&
                    heightDelta <= 0.10)
                {
                    score += 20;
                }
                else if (widthDelta <= 0.25 &&
                         heightDelta <= 0.25)
                {
                    score += 10;
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = handle;
            }

            return true;
        }, IntPtr.Zero);

        return best;
    }

    public static IntPtr FindWindowByProcessAndTitle(
        string processName,
        string stableTitle)
    {
        IntPtr found = IntPtr.Zero;

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle))
                return true;

            var current = GetSnapshot(handle);

            if (current.Handle == IntPtr.Zero)
                return true;

            if (!string.Equals(
                    current.ProcessName,
                    processName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(stableTitle) ||
                current.Title.Contains(
                    stableTitle,
                    StringComparison.OrdinalIgnoreCase))
            {
                found = handle;

                // Detener búsqueda.
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    public static IReadOnlyList<WindowSnapshot> GetVisibleWindowsForProcess(
        string processName)
    {
        var result =
            new List<WindowSnapshot>();

        var processIds =
            Process.GetProcesses()
                .Where(p =>
                    string.Equals(
                        p.ProcessName,
                        processName,
                        StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Id)
                .ToHashSet();

        if (processIds.Count == 0)
        {
            return result;
        }

        NativeMethods.EnumWindows(
            (hWnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd))
                {
                    return true;
                }

                NativeMethods.GetWindowThreadProcessId(
                    hWnd,
                    out var pid);

                if (!processIds.Contains((int)pid))
                {
                    return true;
                }

                var titleBuilder =
                    new System.Text.StringBuilder(512);

                NativeMethods.GetWindowText(
                    hWnd,
                    titleBuilder,
                    titleBuilder.Capacity);

                var classBuilder =
                    new System.Text.StringBuilder(256);

                NativeMethods.GetClassName(
                    hWnd,
                    classBuilder,
                    classBuilder.Capacity);

                if (!NativeMethods.GetWindowRect(
                        hWnd,
                        out var rect))
                {
                    return true;
                }

                result.Add(
                    new WindowSnapshot
                    {
                        Handle =
                            hWnd,

                        ProcessName =
                            processName,

                        Title =
                            titleBuilder.ToString(),

                        ClassName =
                            classBuilder.ToString(),

                        Left =
                            rect.Left,

                        Top =
                            rect.Top,

                        Width =
                            rect.Width,

                        Height =
                            rect.Height
                    });

                return true;
            },
            IntPtr.Zero);

        return result;
    }
}