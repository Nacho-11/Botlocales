using System.Runtime.InteropServices;
using System.Text;

namespace ParrillitaIA.Trainer;

/// <summary>
/// V6.18.23:
/// Diagnóstico y apertura controlada del menú Reportes.
/// Identifica el ítem real bajo el segundo punto y reproduce
/// un gesto humano: mover/hover -> pausa -> down -> pausa -> up.
/// </summary>
internal static class OpenClosuresDiagnostic
{
    private const uint MN_GETHMENU = 0x01E1;

    [DllImport("user32.dll")]
    private static extern int MenuItemFromPoint(
        IntPtr hWnd,
        IntPtr hMenu,
        NativeMethods.POINT ptScreen);

    [DllImport("user32.dll")]
    private static extern uint GetMenuItemID(
        IntPtr hMenu,
        int nPos);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(
        IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuString(
        IntPtr hMenu,
        uint uIDItem,
        StringBuilder lpString,
        int cchMax,
        uint flags);

    private const uint MF_BYPOSITION = 0x00000400;

    internal static async Task RunAsync(
        WorkflowModel workflow,
        CancellationToken cancellationToken)
    {
        var clicks =
            workflow.Steps
                .Where(x =>
                    x.Action.Equals(
                        "LeftClick",
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Order)
                .ToList();

        if (clicks.Count < 2)
        {
            throw new InvalidOperationException(
                "OPEN_CIERRES necesita al menos dos pasos LeftClick.");
        }

        var mainWindow =
            WindowInfo.FindWindowByProcessAndTitle(
                workflow.TargetProcessName,
                "SOFT RESTAURANT");

        if (mainWindow == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "OPEN_CIERRES: no se encontró SOFT RESTAURANT.");
        }

        if (!NativeMethods.GetWindowRect(
                mainWindow,
                out var mainRect))
        {
            throw new InvalidOperationException(
                "OPEN_CIERRES: no se pudo leer la geometría principal.");
        }

        var click1 = ToScreenPoint(mainRect, clicks[0]);
        var click2 = ToScreenPoint(mainRect, clicks[1]);

        Console.WriteLine();
        Console.WriteLine(
            "=== OPEN_CIERRES V6.18.23 ===");

        Console.WriteLine(
            $"[OPEN] CLICK1=({click1.X},{click1.Y}) CLICK2=({click2.X},{click2.Y})");

        NativeMethods.SetForegroundWindow(
            mainWindow);

        await Task.Delay(
            250,
            cancellationToken);

        IntPtr menu =
            IntPtr.Zero;

        const int maxMenuAttempts =
            3;

        for (var attempt = 1;
             attempt <= maxMenuAttempts;
             attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine(
                $"[OPEN] Intento menú Reportes {attempt}/{maxMenuAttempts}...");

            NativeMethods.SetForegroundWindow(
                mainWindow);

            await Task.Delay(
                attempt == 1
                    ? 250
                    : 700,
                cancellationToken);

            Console.WriteLine(
                "[OPEN] Abriendo menú Reportes...");

            await HumanClickAsync(
                click1,
                hoverMs: 100,
                downMs: 60,
                cancellationToken);

            menu =
                await WaitForPopupMenuAtPointAsync(
                    click2,
                    1800,
                    cancellationToken);

            if (menu != IntPtr.Zero)
            {
                Console.WriteLine(
                    $"[OPEN][MENU] Detectado en intento {attempt}.");

                break;
            }

            Console.WriteLine(
                $"[OPEN][WARN] Intento {attempt}: no apareció menú #32768.");

            await Task.Delay(
                500,
                cancellationToken);
        }

        if (menu == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"OPEN_CIERRES: CLICK 1 no dejó un menú #32768 " +
                $"después de {maxMenuAttempts} intentos.");
        }

        Console.WriteLine(
            $"[OPEN][MENU] HWND=0x{menu.ToInt64():X} " +
            $"Rect={DescribeRect(menu)}");

        var menuHandle =
            NativeMethods.SendMessage(
                menu,
                MN_GETHMENU,
                IntPtr.Zero,
                IntPtr.Zero);

        Console.WriteLine(
            $"[OPEN][MENU] HMENU=0x{menuHandle.ToInt64():X}");

        if (menuHandle != IntPtr.Zero)
        {
            var count =
                GetMenuItemCount(
                    menuHandle);

            var index =
                MenuItemFromPoint(
                    mainWindow,
                    menuHandle,
                    click2);

            Console.WriteLine(
                $"[OPEN][MENU] Count={count}; IndexUnderPoint={index}");

            if (index >= 0)
            {
                var commandId =
                    GetMenuItemID(
                        menuHandle,
                        index);

                var text =
                    GetMenuItemText(
                        menuHandle,
                        index);

                Console.WriteLine(
                    $"[OPEN][MENU] Target Index={index}; " +
                    $"CommandId={commandId}; Text=\"{text}\"");
            }
            else
            {
                Console.WriteLine(
                    "[OPEN][MENU] MenuItemFromPoint no encontró ítem bajo CLICK 2.");
            }
        }
        else
        {
            Console.WriteLine(
                "[OPEN][MENU] MN_GETHMENU no devolvió HMENU. " +
                "Continuamos probando hover + click físico.");
        }

        // Muy importante:
        // Primero mover el cursor al ítem y darle tiempo al menú
        // para procesar WM_MOUSEMOVE/hover antes de pulsar.
        Console.WriteLine(
            "[OPEN] Hover sobre opción objetivo...");

        NativeMethods.SetCursorPos(
            click2.X - 2,
            click2.Y);

        await Task.Delay(
            100,
            cancellationToken);

        NativeMethods.SetCursorPos(
            click2.X,
            click2.Y);

        await Task.Delay(
            350,
            cancellationToken);

        var targetBeforeClick =
            NativeMethods.WindowFromPoint(
                click2);

        Console.WriteLine(
            $"[OPEN] HWND bajo CLICK2 antes de pulsar=0x{targetBeforeClick.ToInt64():X} " +
            $"Class=\"{GetClassName(targetBeforeClick)}\"");

        if (targetBeforeClick != menu)
        {
            throw new InvalidOperationException(
                "OPEN_CIERRES: el menú dejó de estar bajo CLICK 2 antes de pulsar.");
        }

        Console.WriteLine(
            "[OPEN] Mouse DOWN...");

        SendMouse(
            NativeMethods.MOUSEEVENTF_LEFTDOWN);

        await Task.Delay(
            100,
            cancellationToken);

        Console.WriteLine(
            "[OPEN] Mouse UP...");

        SendMouse(
            NativeMethods.MOUSEEVENTF_LEFTUP);

        await Task.Delay(
            250,
            cancellationToken);

        var afterClick =
            NativeMethods.WindowFromPoint(
                click2);

        Console.WriteLine(
            $"[OPEN] Después CLICK2: HWND=0x{afterClick.ToInt64():X} " +
            $"Class=\"{GetClassName(afterClick)}\"");

        // V6.18.12:
        // En la prueba V6.18.10 el formulario CIERRES sí quedó abierto
        // visualmente, aunque WaitForVisibleMonthViewAsync devolvió cero.
        // Por eso OPEN_CIERRES termina aquí y la siguiente etapa
        // (EnsureYesterdaySelectedAsync) valida/interactúa con la fecha.
        await Task.Delay(
            1000,
            cancellationToken);

        Console.WriteLine(
            "[OPEN][OK] Formas de pago por turno ejecutado. Continuando con FECHA.");
    }

    private static async Task<IntPtr> WaitForPopupMenuAtPointAsync(
        NativeMethods.POINT point,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var deadline =
            DateTimeOffset.UtcNow.AddMilliseconds(
                timeoutMs);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hWnd =
                NativeMethods.WindowFromPoint(
                    point);

            if (hWnd != IntPtr.Zero &&
                GetClassName(hWnd).Equals(
                    "#32768",
                    StringComparison.OrdinalIgnoreCase))
            {
                return hWnd;
            }

            await Task.Delay(
                50,
                cancellationToken);
        }

        return IntPtr.Zero;
    }

    private static async Task HumanClickAsync(
        NativeMethods.POINT point,
        int hoverMs,
        int downMs,
        CancellationToken cancellationToken)
    {
        NativeMethods.SetCursorPos(
            point.X,
            point.Y);

        await Task.Delay(
            hoverMs,
            cancellationToken);

        SendMouse(
            NativeMethods.MOUSEEVENTF_LEFTDOWN);

        await Task.Delay(
            downMs,
            cancellationToken);

        SendMouse(
            NativeMethods.MOUSEEVENTF_LEFTUP);
    }

    private static void SendMouse(
        uint flags)
    {
        var input =
            new[]
            {
                new NativeMethods.INPUT
                {
                    type =
                        NativeMethods.INPUT_MOUSE,

                    Data =
                        new NativeMethods.INPUTUNION
                        {
                            mi =
                                new NativeMethods.MOUSEINPUT
                                {
                                    dwFlags =
                                        flags
                                }
                        }
                }
            };

        var sent =
            NativeMethods.SendInput(
                1,
                input,
                Marshal.SizeOf<NativeMethods.INPUT>());

        if (sent != 1)
        {
            throw new InvalidOperationException(
                $"SendInput mouse falló: {sent}/1.");
        }
    }

    private static NativeMethods.POINT ToScreenPoint(
        NativeMethods.RECT rect,
        WorkflowStep step)
    {
        return new NativeMethods.POINT
        {
            X =
                rect.Left +
                (int)Math.Round(
                    rect.Width *
                    step.RelativeX),

            Y =
                rect.Top +
                (int)Math.Round(
                    rect.Height *
                    step.RelativeY)
        };
    }

    private static string GetMenuItemText(
        IntPtr hMenu,
        int index)
    {
        var buffer =
            new StringBuilder(
                512);

        GetMenuString(
            hMenu,
            (uint)index,
            buffer,
            buffer.Capacity,
            MF_BYPOSITION);

        return buffer.ToString();
    }

    private static string DescribeRect(
        IntPtr hWnd)
    {
        if (!NativeMethods.GetWindowRect(
                hWnd,
                out var rect))
        {
            return "(?)";
        }

        return
            $"({rect.Left},{rect.Top}," +
            $"{rect.Width},{rect.Height})";
    }

    private static string GetClassName(
        IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return string.Empty;

        var buffer =
            new StringBuilder(
                256);

        NativeMethods.GetClassName(
            hWnd,
            buffer,
            buffer.Capacity);

        return buffer.ToString();
    }
}
