using System.Text;

namespace ParrillitaIA.Trainer;

internal static class SoftRestaurantReportContext
{
    public static async Task PrepareMainWindowAsync(
        string processName,
        string mainTitle,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("[PRECHECK] Preparando pantalla principal...");

        // La ventana "Descargar eventos" puede permanecer sobre SoftRestaurant
        // y bloquear los primeros clics del flujo.
        var downloadEvents =
            FindTopLevelWindow(
                processName,
                titleContains: "Descargar eventos");

        if (downloadEvents != IntPtr.Zero)
        {
            Console.WriteLine(
                "[PRECHECK] Detectada ventana \"Descargar eventos\".");

            // Damos un margen corto por si se cierra al finalizar la carga.
            for (var i = 0; i < 10; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await Task.Delay(
                    500,
                    cancellationToken);

                if (!NativeMethods.IsWindowVisible(downloadEvents))
                {
                    downloadEvents = IntPtr.Zero;
                    break;
                }
            }

            if (downloadEvents != IntPtr.Zero &&
                NativeMethods.IsWindowVisible(downloadEvents))
            {
                Console.WriteLine(
                    "[PRECHECK] Cerrando \"Descargar eventos\" para liberar automatización.");

                NativeMethods.SendMessage(
                    downloadEvents,
                    NativeMethods.WM_CLOSE,
                    IntPtr.Zero,
                    IntPtr.Zero);

                await Task.Delay(
                    800,
                    cancellationToken);
            }
        }

        var main =
            WindowInfo.FindWindowByProcessAndTitle(
                processName,
                mainTitle);

        if (main == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "No se encontró la ventana principal de SoftRestaurant.");
        }

        NativeMethods.SetForegroundWindow(main);

        await Task.Delay(
            700,
            cancellationToken);

        Console.WriteLine(
            "[PRECHECK] Ventana principal lista para abrir CIERRES.");
    }

    public static async Task<IntPtr> WaitForVisibleMonthViewAsync(
        string processName,
        IntPtr mainWindow,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var deadline =
            DateTimeOffset.UtcNow.AddMilliseconds(
                timeoutMs);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var found =
                FindVisibleMonthView(
                    processName,
                    mainWindow);

            if (found != IntPtr.Zero)
                return found;

            await Task.Delay(
                250,
                cancellationToken);
        }

        return IntPtr.Zero;
    }

    public static IntPtr FindVisibleMonthView(
        string processName,
        IntPtr mainWindow)
    {
        IntPtr found = IntPtr.Zero;

        // Primero buscar entre hijos reales de la ventana principal.
        NativeMethods.EnumChildWindows(
            mainWindow,
            (hWnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd) ||
                    !NativeMethods.IsWindowEnabled(hWnd))
                {
                    return true;
                }

                var cls =
                    GetClassName(hWnd);

                if (!cls.Equals(
                        "msvb_lib_monthview",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!NativeMethods.GetWindowRect(
                        hWnd,
                        out var rect))
                {
                    return true;
                }

                // Filtro de geometría: el MonthView entrenado rondaba 162x153.
                if (rect.Width < 100 ||
                    rect.Width > 350 ||
                    rect.Height < 100 ||
                    rect.Height > 300)
                {
                    return true;
                }

                found = hWnd;
                return false;
            },
            IntPtr.Zero);

        if (found != IntPtr.Zero)
            return found;

        // Fallback: algunas apps VB6 exponen controles como ventanas top-level.
        NativeMethods.EnumWindows(
            (hWnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd) ||
                    !NativeMethods.IsWindowEnabled(hWnd))
                {
                    return true;
                }

                NativeMethods.GetWindowThreadProcessId(
                    hWnd,
                    out var pid);

                try
                {
                    using var p =
                        System.Diagnostics.Process.GetProcessById(
                            (int)pid);

                    if (!p.ProcessName.Equals(
                            processName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    return true;
                }

                var cls =
                    GetClassName(hWnd);

                if (!cls.Equals(
                        "msvb_lib_monthview",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!NativeMethods.GetWindowRect(
                        hWnd,
                        out var rect))
                {
                    return true;
                }

                if (rect.Width < 100 ||
                    rect.Width > 350 ||
                    rect.Height < 100 ||
                    rect.Height > 300)
                {
                    return true;
                }

                found = hWnd;
                return false;
            },
            IntPtr.Zero);

        return found;
    }

    public static string Describe(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return "(null)";

        var cls =
            GetClassName(hWnd);

        var title =
            GetWindowTitle(hWnd);

        if (NativeMethods.GetWindowRect(
                hWnd,
                out var rect))
        {
            return
                $"HWND=0x{hWnd.ToInt64():X} " +
                $"Class=\"{cls}\" Title=\"{title}\" " +
                $"Rect=({rect.Left},{rect.Top},{rect.Width},{rect.Height})";
        }

        return
            $"HWND=0x{hWnd.ToInt64():X} " +
            $"Class=\"{cls}\" Title=\"{title}\"";
    }

    private static IntPtr FindTopLevelWindow(
        string processName,
        string titleContains)
    {
        IntPtr result = IntPtr.Zero;

        NativeMethods.EnumWindows(
            (hWnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd))
                    return true;

                NativeMethods.GetWindowThreadProcessId(
                    hWnd,
                    out var pid);

                try
                {
                    using var p =
                        System.Diagnostics.Process.GetProcessById(
                            (int)pid);

                    if (!p.ProcessName.Equals(
                            processName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    return true;
                }

                var title =
                    GetWindowTitle(hWnd);

                if (title.Contains(
                        titleContains,
                        StringComparison.OrdinalIgnoreCase))
                {
                    result = hWnd;
                    return false;
                }

                return true;
            },
            IntPtr.Zero);

        return result;
    }

    private static string GetClassName(
        IntPtr hWnd)
    {
        var sb =
            new StringBuilder(256);

        NativeMethods.GetClassName(
            hWnd,
            sb,
            sb.Capacity);

        return sb.ToString();
    }

    private static string GetWindowTitle(
        IntPtr hWnd)
    {
        var sb =
            new StringBuilder(512);

        NativeMethods.GetWindowText(
            hWnd,
            sb,
            sb.Capacity);

        return sb.ToString();
    }
}
