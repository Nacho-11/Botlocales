using System.Runtime.InteropServices;

namespace ParrillitaIA.Trainer;

public sealed class WorkflowRunner
{
    private const int MaxVisualUsers = 40;
    private const int VisibleRows = 10;

    // Geometría inicial observada en SAN_PEDRO.
    private const int FirstRowOffsetY = 20;
    private const int RowHeight = 15;
    private const int InsideListOffsetX = -40;

    public async Task RunAsync(
        WorkflowModel workflow,
        CancellationToken cancellationToken)
    {
        if (workflow.Steps.Count == 0)
            throw new InvalidOperationException(
                "El flujo no contiene pasos.");

        if (string.Equals(
                workflow.Name,
                "CIERRES",
                StringComparison.OrdinalIgnoreCase))
        {
            await RunVisualClosuresAsync(
                workflow,
                cancellationToken);

            return;
        }

        foreach (var step in workflow.Steps.OrderBy(x => x.Order))
            await ExecuteGenericStepAsync(step, cancellationToken);
    }

    private async Task RunVisualClosuresAsync(
        WorkflowModel workflow,
        CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine(
            "=== CIERRES VISUALES V3.7.4 ===");

        var steps =
            workflow.Steps
                .OrderBy(x => x.Order)
                .ToList();

        var dateStep =
            steps.FirstOrDefault(
                x => x.Action.Equals(
                    "SetYesterdayDate",
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "No existe SetYesterdayDate.");

        var userAnchor =
            steps.FirstOrDefault(x => x.Order == 6)
            ?? throw new InvalidOperationException(
                "No existe paso 6 de Usuario.");

        var executeSteps =
            steps
                .Where(x => x.Order >= 29 && x.Order <= 34)
                .ToList();

        foreach (var step in steps.Where(x => x.Order <= 3))
            await ExecuteGenericStepAsync(step, cancellationToken);

        await RequireMonthViewAsync(
            dateStep,
            cancellationToken);

        var yesterday =
            DateTime.Today.AddDays(-1);

        await SelectYesterdayAsync(
            dateStep,
            cancellationToken);

        Console.WriteLine(
            $"Fecha de prueba: {yesterday:dd/MM/yyyy}");

        ulong? previousHash = null;
        var sameCount = 0;
        var tested = 0;

        for (var index = 0;
             index < MaxVisualUsers;
             index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine();
            Console.WriteLine(
                $"--- POSICIÓN VISUAL {index} ---");

            await RequireMonthViewAsync(
                dateStep,
                cancellationToken);

            await SelectYesterdayAsync(
                dateStep,
                cancellationToken);

            var selected =
                await SelectVisualIndexAsync(
                    userAnchor,
                    index,
                    cancellationToken);

            var hash =
                CaptureFieldFingerprint(
                    selected.Window,
                    selected.AnchorX,
                    selected.AnchorY);

            var changed =
                previousHash is null ||
                previousHash.Value != hash;

            Console.WriteLine(
                $"Huella usuario=0x{hash:X16} " +
                $"Cambio={(changed ? "SI" : "NO")}");

            if (!changed)
            {
                sameCount++;

                Console.WriteLine(
                    $"Selector sin cambio consecutivo: {sameCount}/2");
            }
            else
            {
                sameCount = 0;
            }

            // Dos selecciones consecutivas sin cambio = llegamos al final.
            if (sameCount >= 2)
            {
                Console.WriteLine();
                Console.WriteLine(
                    $"FIN VISUAL detectado en índice {index}.");

                break;
            }

            previousHash = hash;
            tested++;

            Console.WriteLine(
                $"Ejecutando reporte para posición {index}...");

            foreach (var step in executeSteps)
                await ExecuteGenericStepAsync(
                    step,
                    cancellationToken);

            var saveDialog =
                await WaitForWindowByTitleAsync(
                    workflow.TargetProcessName,
                    "Guardar como",
                    8000,
                    cancellationToken);

            if (saveDialog != IntPtr.Zero)
            {
                Console.WriteLine(
                    $"RESULTADO índice {index}: HAY CIERRE / apareció Guardar como.");

                // En V374 de prueba NO guardamos.
                // Cerramos el diálogo para poder seguir recorriendo usuarios.
                NativeMethods.SendMessage(
                    saveDialog,
                    NativeMethods.WM_CLOSE,
                    IntPtr.Zero,
                    IntPtr.Zero);

                await Task.Delay(
                    700,
                    cancellationToken);
            }
            else
            {
                Console.WriteLine(
                    $"RESULTADO índice {index}: SIN Guardar como.");

                await CloseAnyDialogAsync(
                    workflow.TargetProcessName,
                    cancellationToken);
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            $"=== FIN PRUEBA VISUAL: posiciones probadas={tested} ===");
    }

    private sealed record SelectionResult(
        IntPtr Window,
        int AnchorX,
        int AnchorY);

    private static async Task<SelectionResult> SelectVisualIndexAsync(
        WorkflowStep anchor,
        int index,
        CancellationToken cancellationToken)
    {
        var window =
            await WaitForWindowAsync(
                anchor,
                cancellationToken);

        if (window == IntPtr.Zero)
            throw new InvalidOperationException(
                "No apareció la ventana del selector Usuario.");

        if (!NativeMethods.GetWindowRect(
                window,
                out var rect))
            throw new InvalidOperationException(
                "No se pudo leer la ventana Usuario.");

        var anchorX =
            rect.Left +
            (int)Math.Round(
                rect.Width * anchor.RelativeX);

        var anchorY =
            rect.Top +
            (int)Math.Round(
                rect.Height * anchor.RelativeY);

        NativeMethods.SetForegroundWindow(
            window);

        NativeMethods.SetCursorPos(
            anchorX,
            anchorY);

        Click();

        await Task.Delay(
            450,
            cancellationToken);

        var listX =
            anchorX +
            InsideListOffsetX;

        var listY =
            anchorY +
            FirstRowOffsetY;

        NativeMethods.SetCursorPos(
            listX,
            listY);

        // Intentar llevar siempre el desplegable al inicio.
        // Una cantidad grande de scroll hacia arriba es segura: al llegar al principio
        // los eventos restantes ya no deberían cambiar la posición.
        for (var i = 0; i < 25; i++)
        {
            MouseWheel(+120);
            await Task.Delay(20, cancellationToken);
        }

        await Task.Delay(
            250,
            cancellationToken);

        int visibleIndex;

        if (index < VisibleRows)
        {
            visibleIndex = index;
        }
        else
        {
            // Mantener la fila inferior visible y desplazar la lista hacia abajo.
            visibleIndex = VisibleRows - 1;

            var requiredScroll =
                index -
                (VisibleRows - 1);

            Console.WriteLine(
                $"Scroll visual requerido={requiredScroll}");

            for (var i = 0;
                 i < requiredScroll;
                 i++)
            {
                // Usamos una rueda por iteración.
                MouseWheel(-120);

                await Task.Delay(
                    120,
                    cancellationToken);
            }
        }

        var rowX =
            listX;

        var rowY =
            anchorY +
            FirstRowOffsetY +
            visibleIndex *
            RowHeight;

        Console.WriteLine(
            $"Click posición: index={index}, " +
            $"visible={visibleIndex}, ({rowX},{rowY})");

        NativeMethods.SetCursorPos(
            rowX,
            rowY);

        await Task.Delay(
            150,
            cancellationToken);

        Click();

        await Task.Delay(
            550,
            cancellationToken);

        return new SelectionResult(
            window,
            anchorX,
            anchorY);
    }

    private static ulong CaptureFieldFingerprint(
        IntPtr window,
        int anchorX,
        int anchorY)
    {
        // Muestreamos una caja alrededor del valor visible de Usuario.
        // FNV-1a 64-bit sobre 18 x 5 píxeles distribuidos.
        const int leftOffset = -130;
        const int topOffset = -10;
        const int width = 120;
        const int height = 20;

        var hdc =
            NativeMethods.GetDC(
                IntPtr.Zero);

        if (hdc == IntPtr.Zero)
            return 0;

        try
        {
            ulong hash =
                1469598103934665603UL;

            for (var gy = 0; gy < 5; gy++)
            {
                for (var gx = 0; gx < 18; gx++)
                {
                    var x =
                        anchorX +
                        leftOffset +
                        gx *
                        width /
                        17;

                    var y =
                        anchorY +
                        topOffset +
                        gy *
                        height /
                        4;

                    var pixel =
                        NativeMethods.GetPixel(
                            hdc,
                            x,
                            y);

                    hash ^=
                        pixel;

                    hash *=
                        1099511628211UL;
                }
            }

            return hash;
        }
        finally
        {
            NativeMethods.ReleaseDC(
                IntPtr.Zero,
                hdc);
        }
    }

    private static async Task RequireMonthViewAsync(
        WorkflowStep dateStep,
        CancellationToken cancellationToken)
    {
        var h =
            await WaitForWindowAsync(
                dateStep,
                cancellationToken);

        if (h == IntPtr.Zero)
            throw new InvalidOperationException(
                "No se detectó MonthView; se detiene para evitar clics incorrectos.");
    }

    private static async Task SelectYesterdayAsync(
        WorkflowStep dateStep,
        CancellationToken cancellationToken)
    {
        var month =
            await WaitForWindowAsync(
                dateStep,
                cancellationToken);

        if (month == IntPtr.Zero)
            throw new InvalidOperationException(
                "No apareció MonthView.");

        if (!NativeMethods.GetWindowRect(
                month,
                out var rect))
            throw new InvalidOperationException(
                "No se pudo leer MonthView.");

        var date =
            DateTime.Today.AddDays(-1);

        var first =
            new DateTime(
                date.Year,
                date.Month,
                1);

        var firstColumn =
            ((int)first.DayOfWeek + 6) % 7;

        var dayIndex =
            firstColumn +
            date.Day -
            1;

        var row =
            dayIndex / 7;

        var col =
            dayIndex % 7;

        const double left = 0.025;
        const double right = 0.025;
        const double top = 0.30;
        const double bottom = 0.97;

        var cellWidth =
            rect.Width *
            (1 - left - right) /
            7.0;

        var cellHeight =
            rect.Height *
            (bottom - top) /
            6.0;

        var x =
            rect.Left +
            (int)Math.Round(
                rect.Width * left +
                cellWidth * (col + 0.5));

        var y =
            rect.Top +
            (int)Math.Round(
                rect.Height * top +
                cellHeight * (row + 0.5));

        Console.WriteLine(
            $"AYER {date:dd/MM/yyyy} click=({x},{y})");

        NativeMethods.SetForegroundWindow(
            month);

        NativeMethods.SetCursorPos(
            x,
            y);

        Click();

        await Task.Delay(
            450,
            cancellationToken);
    }

    private static async Task ExecuteGenericStepAsync(
        WorkflowStep step,
        CancellationToken cancellationToken)
    {
        await Task.Delay(
            Math.Clamp(
                step.DelayBeforeMs,
                100,
                30000),
            cancellationToken);

        var handle =
            await WaitForWindowAsync(
                step,
                cancellationToken);

        if (handle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"No apareció la ventana del paso {step.Order}.");

        if (step.Action == "WaitForWindow")
            return;

        if (step.Action == "SetYesterdayDate")
        {
            await SelectYesterdayAsync(
                step,
                cancellationToken);

            return;
        }

        if (step.Action == "KeyPress")
        {
            SendKey(
                step.VirtualKey,
                step.Ctrl,
                step.Shift,
                step.Alt);

            return;
        }

        if (step.Action != "LeftClick")
            throw new InvalidOperationException(
                $"Acción desconocida {step.Action}");

        if (!NativeMethods.GetWindowRect(
                handle,
                out var rect))
            throw new InvalidOperationException(
                "No se pudo leer ventana.");

        var x =
            rect.Left +
            (int)Math.Round(
                rect.Width * step.RelativeX);

        var y =
            rect.Top +
            (int)Math.Round(
                rect.Height * step.RelativeY);

        NativeMethods.SetForegroundWindow(
            handle);

        NativeMethods.SetCursorPos(
            x,
            y);

        Click();
    }

    private static async Task<IntPtr> WaitForWindowAsync(
        WorkflowStep step,
        CancellationToken cancellationToken)
    {
        var deadline =
            DateTimeOffset.UtcNow.AddMilliseconds(
                Math.Clamp(
                    step.WindowWaitTimeoutMs,
                    1000,
                    60000));

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var h =
                WindowInfo.FindBestWindow(
                    step);

            if (h != IntPtr.Zero)
                return h;

            await Task.Delay(
                250,
                cancellationToken);
        }

        return IntPtr.Zero;
    }

    private static async Task<IntPtr> WaitForWindowByTitleAsync(
        string processName,
        string title,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var deadline =
            DateTimeOffset.UtcNow.AddMilliseconds(
                timeoutMs);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var h =
                WindowInfo.FindWindowByProcessAndTitle(
                    processName,
                    title);

            if (h != IntPtr.Zero)
                return h;

            await Task.Delay(
                250,
                cancellationToken);
        }

        return IntPtr.Zero;
    }

    private static async Task CloseAnyDialogAsync(
        string processName,
        CancellationToken cancellationToken)
    {
        IntPtr found =
            IntPtr.Zero;

        NativeMethods.EnumWindows(
            (h, _) =>
            {
                if (!NativeMethods.IsWindowVisible(h))
                    return true;

                var s =
                    WindowInfo.GetSnapshot(h);

                if (!s.ProcessName.Equals(
                        processName,
                        StringComparison.OrdinalIgnoreCase))
                    return true;

                if (!s.ClassName.Equals(
                        "#32770",
                        StringComparison.OrdinalIgnoreCase))
                    return true;

                found = h;
                return false;
            },
            IntPtr.Zero);

        if (found != IntPtr.Zero)
        {
            NativeMethods.SendMessage(
                found,
                NativeMethods.WM_CLOSE,
                IntPtr.Zero,
                IntPtr.Zero);

            await Task.Delay(
                400,
                cancellationToken);
        }
    }

    private static void MouseWheel(
        int delta)
    {
        Send(
        [
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
                                mouseData =
                                    unchecked((uint)delta),

                                dwFlags =
                                    NativeMethods.MOUSEEVENTF_WHEEL
                            }
                    }
            }
        ]);
    }

    private static void Click()
    {
        Send(
        [
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
                                    NativeMethods.MOUSEEVENTF_LEFTDOWN
                            }
                    }
            },

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
                                    NativeMethods.MOUSEEVENTF_LEFTUP
                            }
                    }
            }
        ]);
    }

    private static void SendKey(
        ushort key,
        bool ctrl,
        bool shift,
        bool alt)
    {
        var list =
            new List<NativeMethods.INPUT>();

        if (ctrl)
            list.Add(KeyDown(NativeMethods.VK_CONTROL));

        if (shift)
            list.Add(KeyDown(NativeMethods.VK_SHIFT));

        if (alt)
            list.Add(KeyDown(NativeMethods.VK_MENU));

        list.Add(KeyDown(key));
        list.Add(KeyUp(key));

        if (alt)
            list.Add(KeyUp(NativeMethods.VK_MENU));

        if (shift)
            list.Add(KeyUp(NativeMethods.VK_SHIFT));

        if (ctrl)
            list.Add(KeyUp(NativeMethods.VK_CONTROL));

        Send(
            list.ToArray());
    }

    private static NativeMethods.INPUT KeyDown(
        ushort key) =>
        new()
        {
            type =
                NativeMethods.INPUT_KEYBOARD,

            Data =
                new NativeMethods.INPUTUNION
                {
                    ki =
                        new NativeMethods.KEYBDINPUT
                        {
                            wVk = key
                        }
                }
        };

    private static NativeMethods.INPUT KeyUp(
        ushort key) =>
        new()
        {
            type =
                NativeMethods.INPUT_KEYBOARD,

            Data =
                new NativeMethods.INPUTUNION
                {
                    ki =
                        new NativeMethods.KEYBDINPUT
                        {
                            wVk = key,

                            dwFlags =
                                NativeMethods.KEYEVENTF_KEYUP
                        }
                }
        };

    private static void Send(
        NativeMethods.INPUT[] inputs)
    {
        var sent =
            NativeMethods.SendInput(
                (uint)inputs.Length,
                inputs,
                Marshal.SizeOf<NativeMethods.INPUT>());

        if (sent != inputs.Length)
            throw new InvalidOperationException(
                $"SendInput {sent}/{inputs.Length}");
    }
}
