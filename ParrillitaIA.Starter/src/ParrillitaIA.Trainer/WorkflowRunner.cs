using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ParrillitaIA.Trainer;

public sealed class WorkflowRunner
{
    private const int MaxUsers = 50;
    private const int FirstRowOffsetY = 20;
    private const int RowHeight = 15;
    private const int InsideListOffsetX = -40;
    private const int VisibleRows = 10;

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
            await RunClosuresSequentialAsync(
                workflow,
                cancellationToken);

            return;
        }

        foreach (var step in workflow.Steps.OrderBy(x => x.Order))
            await ExecuteGenericStepAsync(step, cancellationToken);
    }

    private async Task RunClosuresSequentialAsync(
        WorkflowModel workflow,
        CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine(
            "=== CIERRES V6.7 - FECHA OK + USUARIOS UP 3 Y LUEGO DOWN 1 ===");

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
            steps.Where(
                    x => x.Order >= 29 &&
                         x.Order <= 34)
                .ToList();

        if (executeSteps.Count == 0)
            throw new InvalidOperationException(
                "No existen pasos 29-34.");

        await SoftRestaurantReportContext.PrepareMainWindowAsync(
            workflow.TargetProcessName,
            "SOFT RESTAURANT",
            cancellationToken);

        foreach (var step in steps.Where(x => x.Order <= 3))
            await ExecuteGenericStepAsync(step, cancellationToken);

        var mainWindow =
            WindowInfo.FindWindowByProcessAndTitle(
                workflow.TargetProcessName,
                "SOFT RESTAURANT");

        var realMonthView =
            await SoftRestaurantReportContext.WaitForVisibleMonthViewAsync(
                workflow.TargetProcessName,
                mainWindow,
                15000,
                cancellationToken);

        if (realMonthView == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Los pasos 1-3 no abrieron el formulario CIERRES: " +
                "no apareció un MonthView visible.");
        }

        Console.WriteLine(
            "[CIERRES] MonthView real: " +
            SoftRestaurantReportContext.Describe(realMonthView));

        await EnsureYesterdaySelectedAsync(
            dateStep,
            cancellationToken);

        var reportDate =
            DateTime.Today.AddDays(-1);

        var outputRoot =
            GetCashClosuresRoot();

        var outputDirectory =
            Path.Combine(
                outputRoot,
                reportDate.ToString("yyyy"),
                reportDate.ToString("MM"));

        Directory.CreateDirectory(
            outputDirectory);

        Console.WriteLine(
            $"[GUARDADO] Carpeta destino: {outputDirectory}");

        var usersChecked = 0;
        var closuresFound = 0;
        var filesSaved = 0;

        // V6.6.1:
        // Abrir Usuario, subir hasta el inicio real con flechas y seleccionar
        // el primer usuario. Sin rueda del mouse y sin clicks por fila.
        var current =
            await SelectFirstUserByKeyboardAsync(
                userAnchor,
                cancellationToken);

        var currentHash =
            CaptureUserFieldFingerprint(
                current.AnchorX,
                current.AnchorY);

        Console.WriteLine(
            $"[USUARIOS] Primer usuario real seleccionado. Huella=0x{currentHash:X16}");

        for (var ordinal = 0;
             ordinal < MaxUsers;
             ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine();
            Console.WriteLine(
                $"=== USUARIO VISUAL #{ordinal + 1} ===");

            Console.WriteLine(
                $"Huella actual: 0x{currentHash:X16}");

            usersChecked++;

            // La fecha ya fue fijada en AYER antes de comenzar el recorrido.
            // No se vuelve a tocar por cada usuario: evita movimientos
            // innecesarios y hace el proceso determinista.
            foreach (var step in executeSteps)
            {
                await ExecuteGenericStepAsync(
                    step,
                    cancellationToken);
            }

            var saveDialog =
                await WaitForWindowByTitleAsync(
                    workflow.TargetProcessName,
                    "Guardar como",
                    10000,
                    cancellationToken);

            if (saveDialog != IntPtr.Zero)
            {
                closuresFound++;

                Console.WriteLine(
                    $"RESULTADO #{ordinal + 1}: HAY CIERRE.");

                var baseFileName =
                    $"CIERRE_{reportDate:yyyy-MM-dd}_USUARIO_{ordinal + 1:00}";

                var savedPath =
                    await SaveClosureDialogAsync(
                        saveDialog,
                        outputDirectory,
                        baseFileName,
                        cancellationToken);

                filesSaved++;

                Console.WriteLine(
                    $"[GUARDADO] Archivo confirmado: {savedPath}");

                await Task.Delay(
                    800,
                    cancellationToken);
            }
            else
            {
                Console.WriteLine(
                    $"RESULTADO #{ordinal + 1}: SIN CIERRE.");

                await CloseAnyDialogAsync(
                    workflow.TargetProcessName,
                    cancellationToken);
            }

            await RequireMonthViewAsync(
                dateStep,
                cancellationToken);

            var next =
                await SelectNextUserByKeyboardAsync(
                    userAnchor,
                    currentHash,
                    cancellationToken);

            if (next is null)
            {
                Console.WriteLine();
                Console.WriteLine(
                    "FIN DE LISTA: DOWN x1 ya no cambió el usuario.");

                break;
            }

            current =
                next.Value.Selection;

            currentHash =
                next.Value.Hash;
        }

        Console.WriteLine();
        Console.WriteLine(
            "=== RESUMEN V377 ===");

        Console.WriteLine(
            $"Usuarios distintos revisados: {usersChecked}");

        Console.WriteLine(
            $"Cierres detectados: {closuresFound}");

        Console.WriteLine(
            $"Archivos guardados: {filesSaved}");

        Console.WriteLine(
            $"Destino: {outputDirectory}");
    }

    private readonly record struct SelectionResult(
        IntPtr Window,
        int AnchorX,
        int AnchorY);

    private readonly record struct NextUserResult(
        SelectionResult Selection,
        ulong Hash);

    private static async Task<string> SaveClosureDialogAsync(
        IntPtr dialog,
        string outputDirectory,
        string baseFileName,
        CancellationToken cancellationToken)
    {
        NativeMethods.SetForegroundWindow(
            dialog);

        await Task.Delay(
            350,
            cancellationToken);

        if (!NativeMethods.GetWindowRect(
                dialog,
                out var dialogRect))
        {
            throw new InvalidOperationException(
                "No se pudo leer la ventana Guardar como.");
        }

        IntPtr bestEdit =
            IntPtr.Zero;

        var bestScore =
            int.MinValue;

        IntPtr saveButton =
            IntPtr.Zero;

        NativeMethods.EnumChildWindows(
            dialog,
            (child, _) =>
            {
                if (!NativeMethods.IsWindowVisible(child) ||
                    !NativeMethods.IsWindowEnabled(child))
                {
                    return true;
                }

                var className =
                    GetClassName(child);

                var text =
                    GetWindowText(child);

                if (className.Equals(
                        "Edit",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (NativeMethods.GetWindowRect(
                            child,
                            out var r))
                    {
                        var relativeTop =
                            (r.Top - dialogRect.Top) /
                            (double)Math.Max(
                                1,
                                dialogRect.Height);

                        var score =
                            r.Width +
                            (relativeTop > 0.50
                                ? 2000
                                : 0);

                        if (score > bestScore)
                        {
                            bestScore =
                                score;

                            bestEdit =
                                child;
                        }
                    }
                }

                if (className.Equals(
                        "Button",
                        StringComparison.OrdinalIgnoreCase) &&
                    (text.Contains(
                         "Guardar",
                         StringComparison.OrdinalIgnoreCase) ||
                     text.Contains(
                         "Save",
                         StringComparison.OrdinalIgnoreCase)))
                {
                    saveButton =
                        child;
                }

                return true;
            },
            IntPtr.Zero);

        if (bestEdit == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "No se encontró el campo Nombre de archivo en Guardar como.");
        }

        // Dejamos que el tipo de archivo seleccionado por SoftRestaurant
        // determine la extensión. Primero probamos .xlsx y luego verificamos
        // alternativas comunes si la aplicación cambia la extensión.
        var requestedPath =
            Path.Combine(
                outputDirectory,
                baseFileName + ".xlsx");

        Console.WriteLine(
            $"[GUARDADO] Solicitando: {requestedPath}");

        NativeMethods.SendMessage(
            bestEdit,
            NativeMethods.WM_SETTEXT,
            IntPtr.Zero,
            requestedPath);

        await Task.Delay(
            350,
            cancellationToken);

        if (saveButton != IntPtr.Zero)
        {
            NativeMethods.SendMessage(
                saveButton,
                NativeMethods.BM_CLICK,
                IntPtr.Zero,
                IntPtr.Zero);
        }
        else
        {
            NativeMethods.SetFocus(
                bestEdit);

            SendKey(
                0x0D,
                false,
                false,
                false);
        }

        await HandleOverwriteConfirmationAsync(
            cancellationToken);

        var confirmed =
            await WaitForSavedFileAsync(
                outputDirectory,
                baseFileName,
                12000,
                cancellationToken);

        if (confirmed is null)
        {
            throw new IOException(
                $"SoftRestaurant cerró Guardar como pero no apareció el archivo " +
                $"{baseFileName} en {outputDirectory}.");
        }

        return confirmed;
    }

    private static async Task HandleOverwriteConfirmationAsync(
        CancellationToken cancellationToken)
    {
        var deadline =
            DateTimeOffset.UtcNow.AddSeconds(
                3);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IntPtr confirm =
                IntPtr.Zero;

            NativeMethods.EnumWindows(
                (h, _) =>
                {
                    if (!NativeMethods.IsWindowVisible(h))
                        return true;

                    var title =
                        GetWindowText(h);

                    var cls =
                        GetClassName(h);

                    if (!cls.Equals(
                            "#32770",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (title.Contains(
                            "Confirm",
                            StringComparison.OrdinalIgnoreCase) ||
                        title.Contains(
                            "Reempl",
                            StringComparison.OrdinalIgnoreCase) ||
                        title.Contains(
                            "Confirmar",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        confirm = h;
                        return false;
                    }

                    return true;
                },
                IntPtr.Zero);

            if (confirm != IntPtr.Zero)
            {
                Console.WriteLine(
                    "[GUARDADO] Confirmación de reemplazo detectada.");

                NativeMethods.SetForegroundWindow(
                    confirm);

                SendKey(
                    0x0D,
                    false,
                    false,
                    false);

                await Task.Delay(
                    500,
                    cancellationToken);

                return;
            }

            await Task.Delay(
                200,
                cancellationToken);
        }
    }

    private static async Task<string?> WaitForSavedFileAsync(
        string directory,
        string baseFileName,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var deadline =
            DateTimeOffset.UtcNow.AddMilliseconds(
                timeoutMs);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(directory))
            {
                var match =
                    Directory
                        .EnumerateFiles(
                            directory,
                            baseFileName + ".*",
                            SearchOption.TopDirectoryOnly)
                        .OrderByDescending(
                            File.GetLastWriteTimeUtc)
                        .FirstOrDefault();

                if (match is not null)
                {
                    try
                    {
                        var info =
                            new FileInfo(match);

                        if (info.Exists &&
                            info.Length > 0)
                        {
                            return match;
                        }
                    }
                    catch
                    {
                        // OneDrive puede estar sincronizando/bloqueando el archivo.
                    }
                }
            }

            await Task.Delay(
                300,
                cancellationToken);
        }

        return null;
    }

    private static string GetCashClosuresRoot()
    {
        var appSettingsPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "appsettings.json");

        if (!File.Exists(
                appSettingsPath))
        {
            throw new FileNotFoundException(
                $"No existe appsettings.json en: {appSettingsPath}");
        }

        using var document =
            JsonDocument.Parse(
                File.ReadAllText(
                    appSettingsPath));

        if (!document.RootElement.TryGetProperty(
                "Storage",
                out var storage))
        {
            throw new InvalidOperationException(
                "appsettings.json no contiene la sección Storage.");
        }

        if (!storage.TryGetProperty(
                "OneDriveCashClosuresRoot",
                out var rootElement))
        {
            throw new InvalidOperationException(
                "Storage no contiene OneDriveCashClosuresRoot.");
        }

        var root =
            rootElement.GetString();

        if (string.IsNullOrWhiteSpace(
                root))
        {
            throw new InvalidOperationException(
                "Storage:OneDriveCashClosuresRoot está vacío.");
        }

        if (!Directory.Exists(root))
        {
            Console.WriteLine(
                $"[GUARDADO] La raíz aún no existe o OneDrive no está disponible: {root}");

            Directory.CreateDirectory(
                root);
        }

        return root;
    }

    private static async Task<SelectionResult> SelectFirstUserByKeyboardAsync(
        WorkflowStep anchor,
        CancellationToken cancellationToken)
    {
        var selection =
            await OpenUserDropdownAsync(
                anchor,
                cancellationToken);

        Console.WriteLine(
            "[USUARIOS] Desde el usuario inicial: UP x3 exactos.");

        // V6.7:
        // SoftRestaurant abre el selector en un usuario conocido dentro del bloque.
        // En lugar de ir hasta el inicio absoluto, subimos EXACTAMENTE 3 posiciones
        // desde donde empieza. A partir de ahí el recorrido será DOWN x1 por usuario.
        for (var i = 0; i < 3; i++)
        {
            SendKey(
                0x26, // VK_UP
                false,
                false,
                false);

            await Task.Delay(
                220,
                cancellationToken);

            Console.WriteLine(
                $"[USUARIOS] UP {i + 1}/3.");
        }

        SendKey(
            0x0D, // ENTER
            false,
            false,
            false);

        await Task.Delay(
            600,
            cancellationToken);

        Console.WriteLine(
            "[USUARIOS] Usuario inicial de recorrido fijado después de UP x3.");

        return selection;
    }

    private static async Task<NextUserResult?> SelectNextUserByKeyboardAsync(
        WorkflowStep anchor,
        ulong currentHash,
        CancellationToken cancellationToken)
    {
        var selection =
            await OpenUserDropdownAsync(
                anchor,
                cancellationToken);

        // EXACTAMENTE un usuario por iteración, siempre hacia abajo.
        SendKey(
            0x28, // VK_DOWN
            false,
            false,
            false);

        await Task.Delay(
            320,
            cancellationToken);

        SendKey(
            0x0D, // ENTER
            false,
            false,
            false);

        await Task.Delay(
            600,
            cancellationToken);

        var newHash =
            CaptureUserFieldFingerprint(
                selection.AnchorX,
                selection.AnchorY);

        Console.WriteLine(
            $"[USUARIOS] DOWN x1: anterior=0x{currentHash:X16}; nuevo=0x{newHash:X16}");

        if (newHash == currentHash)
        {
            Console.WriteLine(
                "[USUARIOS] No hubo cambio visual: se alcanzó el último usuario.");

            return null;
        }

        return new NextUserResult(
            selection,
            newHash);
    }

    private static async Task<SelectionResult> OpenUserDropdownAsync(
        WorkflowStep anchor,
        CancellationToken cancellationToken)
    {
        var window =
            await WaitForWindowAsync(
                anchor,
                cancellationToken);

        if (window == IntPtr.Zero)
            throw new InvalidOperationException(
                "No apareció selector Usuario.");

        if (!NativeMethods.GetWindowRect(
                window,
                out var rect))
        {
            throw new InvalidOperationException(
                "No se pudo leer selector Usuario.");
        }

        var anchorX =
            rect.Left +
            (int)Math.Round(
                rect.Width *
                anchor.RelativeX);

        var anchorY =
            rect.Top +
            (int)Math.Round(
                rect.Height *
                anchor.RelativeY);

        NativeMethods.SetForegroundWindow(
            window);

        NativeMethods.SetCursorPos(
            anchorX,
            anchorY);

        // El mouse solo abre el desplegable; el recorrido es 100% teclado.
        Click();

        await Task.Delay(
            450,
            cancellationToken);

        return new SelectionResult(
            window,
            anchorX,
            anchorY);
    }

    private static async Task EnsureYesterdaySelectedAsync(
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
        {
            throw new InvalidOperationException(
                "No se pudo leer MonthView.");
        }

        var target =
            DateTime.Today.AddDays(-1);

        var first =
            new DateTime(
                target.Year,
                target.Month,
                1);

        var firstColumn =
            ((int)first.DayOfWeek + 6) % 7;

        var dayIndex =
            firstColumn +
            target.Day -
            1;

        var row =
            dayIndex / 7;

        var col =
            dayIndex % 7;

        const double left = 0.025;
        const double right = 0.025;
        const double top = 0.30;
        const double bottom = 0.97;

        var cw =
            rect.Width *
            (1 - left - right) /
            7.0;

        var ch =
            rect.Height *
            (bottom - top) /
            6.0;

        var x =
            rect.Left +
            (int)Math.Round(
                rect.Width * left +
                cw * (col + 0.5));

        // CORRECCION V6.4:
        // En la prueba real, al intentar seleccionar 25/08/2026
        // SoftRestaurant terminaba en 01/09/2026.
        //
        // 01/09 - 25/08 = EXACTAMENTE 7 dias, es decir UNA FILA.
        // Por eso mantenemos intacto el calculo horizontal y desplazamos
        // la coordenada Y exactamente una altura de celda hacia arriba.
        //
        // Esto no depende de una cantidad fija de pixeles: usa "ch",
        // calculado con el tamaño real del MonthView.
        var originalY =
            rect.Top +
            (int)Math.Round(
                rect.Height * top +
                ch * (row + 0.5));

        // V6.4 corrigió una fila completa hacia arriba y el resultado
        // pasó de +7 días a -7 días. Eso confirma que el punto correcto
        // está exactamente entre ambas posiciones.
        //
        // Por tanto V6.5 corrige MEDIA altura de celda hacia arriba.
        var y =
            originalY -
            (int)Math.Round(ch / 2.0);

        Console.WriteLine(
            $"[FECHA] Hoy={DateTime.Today:dd/MM/yyyy}; AYER={target:dd/MM/yyyy}; " +
            $"row={row}; col={col}; cellHeight={ch:F1}; " +
            $"Yoriginal={originalY}; Ymedia={y}; click=({x},{y})");

        NativeMethods.SetForegroundWindow(
            month);

        NativeMethods.SetCursorPos(
            x,
            y);

        Click();

        await Task.Delay(
            650,
            cancellationToken);

        Console.WriteLine(
            $"[FECHA] Clic con corrección de media fila aplicado para AYER={target:dd/MM/yyyy}.");
    }

    private static ulong CaptureUserFieldFingerprint(
        int anchorX,
        int anchorY)
    {
        var hdc =
            NativeMethods.GetDC(
                IntPtr.Zero);

        if (hdc == IntPtr.Zero)
            return 0;

        try
        {
            ulong hash =
                1469598103934665603UL;

            const int left =
                -140;

            const int top =
                -12;

            const int width =
                125;

            const int height =
                24;

            for (var row = 0;
                 row < 6;
                 row++)
            {
                for (var column = 0;
                     column < 20;
                     column++)
                {
                    var x =
                        anchorX +
                        left +
                        column *
                        (width - 1) /
                        19;

                    var y =
                        anchorY +
                        top +
                        row *
                        (height - 1) /
                        5;

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
        {
            throw new InvalidOperationException(
                "No se detectó MonthView.");
        }
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
        {
            throw new InvalidOperationException(
                $"No apareció la ventana del paso {step.Order}.");
        }

        if (step.Action ==
            "WaitForWindow")
        {
            return;
        }

        if (step.Action ==
            "SetYesterdayDate")
        {
            await EnsureYesterdaySelectedAsync(
                step,
                cancellationToken);

            return;
        }

        if (step.Action ==
            "KeyPress")
        {
            SendKey(
                step.VirtualKey,
                step.Ctrl,
                step.Shift,
                step.Alt);

            return;
        }

        if (step.Action !=
            "LeftClick")
        {
            throw new InvalidOperationException(
                $"Acción desconocida {step.Action}");
        }

        if (!NativeMethods.GetWindowRect(
                handle,
                out var rect))
        {
            throw new InvalidOperationException(
                "No se pudo leer ventana.");
        }

        var x =
            rect.Left +
            (int)Math.Round(
                rect.Width *
                step.RelativeX);

        var y =
            rect.Top +
            (int)Math.Round(
                rect.Height *
                step.RelativeY);

        NativeMethods.SetForegroundWindow(
            handle);

        NativeMethods.SetCursorPos(
            x,
            y);

        Click();

        await Task.Delay(
            150,
            cancellationToken);
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
                if (!NativeMethods.IsWindowVisible(
                        h))
                {
                    return true;
                }

                var snapshot =
                    WindowInfo.GetSnapshot(
                        h);

                if (!snapshot.ProcessName.Equals(
                        processName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!snapshot.ClassName.Equals(
                        "#32770",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                found =
                    h;

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

    private static string GetClassName(
        IntPtr hWnd)
    {
        var sb =
            new StringBuilder(
                256);

        NativeMethods.GetClassName(
            hWnd,
            sb,
            sb.Capacity);

        return sb.ToString();
    }

    private static string GetWindowText(
        IntPtr hWnd)
    {
        var sb =
            new StringBuilder(
                512);

        NativeMethods.GetWindowText(
            hWnd,
            sb,
            sb.Capacity);

        return sb.ToString();
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
            list.Add(
                KeyDown(
                    NativeMethods.VK_CONTROL));

        if (shift)
            list.Add(
                KeyDown(
                    NativeMethods.VK_SHIFT));

        if (alt)
            list.Add(
                KeyDown(
                    NativeMethods.VK_MENU));

        list.Add(
            KeyDown(
                key));

        list.Add(
            KeyUp(
                key));

        if (alt)
            list.Add(
                KeyUp(
                    NativeMethods.VK_MENU));

        if (shift)
            list.Add(
                KeyUp(
                    NativeMethods.VK_SHIFT));

        if (ctrl)
            list.Add(
                KeyUp(
                    NativeMethods.VK_CONTROL));

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
                            wVk =
                                key
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
                            wVk =
                                key,

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
                Marshal.SizeOf<
                    NativeMethods.INPUT>());

        if (sent !=
            inputs.Length)
        {
            throw new InvalidOperationException(
                $"SendInput {sent}/{inputs.Length}");
        }
    }
}
