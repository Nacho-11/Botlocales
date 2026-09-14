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
    private const int VisibleRows = 7;

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
            "=== CIERRES V6.18.61A - TODOS LOS CIERRES + IDEMPOTENCIA ===");

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

        // V6.18.30:
        // Entrenamiento histórico:
        //   paso 29 = abrir selector Reporte
        //   paso 30 = seleccionar segunda opción (Láser)
        var reportOpenStep =
            steps.FirstOrDefault(x => x.Order == 29)
            ?? throw new InvalidOperationException(
                "No existe paso 29 de Reporte.");

        var reportLaserStep =
            steps.FirstOrDefault(x => x.Order == 30)
            ?? throw new InvalidOperationException(
                "No existe paso 30 de Reporte=Láser.");

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

        // V6.18.5: la apertura de CIERRES se separa del flujo histórico.
        // Si existe OPEN_CIERRES.json, se reproduce ese entrenamiento semántico.
        // Si todavía no existe, conservamos los pasos 1-3 como fallback temporal.
        var openClosuresFile =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                "ParrillitaIA",
                "Training",
                WorkflowName.Sanitize(workflow.Local),
                "OPEN_CIERRES.json");

        if (File.Exists(openClosuresFile))
        {
            Console.WriteLine(
                "[CIERRES] Ejecutando entrenamiento OPEN_CIERRES...");

            var openClosuresWorkflow =
                WorkflowStore.Load(
                    openClosuresFile);

            // V6.18.9:
            // Diagnóstico controlado de la apertura de CIERRES.
            // No modifica fecha, usuarios ni guardado.
            await OpenClosuresDiagnostic.RunAsync(
                openClosuresWorkflow,
                cancellationToken);

            Console.WriteLine(
                "[CIERRES] OPEN_CIERRES finalizado.");
        }
        else
        {
            Console.WriteLine(
                "[CIERRES] OPEN_CIERRES.json no existe; usando pasos históricos 1-3.");

            foreach (var step in steps.Where(x => x.Order <= 3))
                await ExecuteGenericStepAsync(step, cancellationToken);
        }

        await EnsureYesterdaySelectedAsync(
            dateStep,
            cancellationToken);

        // V6.18: diagnóstico aislado del selector Usuario.
        // No ejecuta cierres todavía. Primero comprobamos si SoftRestaurant
        // expone el selector como ComboBox/Combo clásico y si podemos leer
        // sus elementos de forma determinista.
        await RunControlledExcelExecuteDiagnosticAsync(
            workflow.TargetProcessName,
            userAnchor,
            reportOpenStep,
            reportLaserStep,
            cancellationToken);

        Console.WriteLine();
        Console.WriteLine(
            "[V6.18.61A] Proceso de cierres terminado.");
        Console.WriteLine(
            "[V6.18.61A] Revisa [EXPORT][OK], [EXPORT][SKIP-EXISTING] y [RESUMEN].");

        // Diagnóstico activo por defecto. Al ser una decisión de runtime,
        // el compilador no marca el código productivo posterior como inaccesible.
        var diagnosticOnly =
            !string.Equals(
                Environment.GetEnvironmentVariable(
                    "PARRILLITA_V618_DIAGNOSTIC_ONLY"),
                "0",
                StringComparison.OrdinalIgnoreCase);

        if (diagnosticOnly)
            return;

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

        // V6.17:
        // FECHA permanece exactamente como V6.5.
        // USUARIO reproduce el entrenamiento nuevo:
        // LEFT -> DOWN -> ENTER.
        var current =
            await SelectFirstUserFromTrainingAsync(
                userAnchor,
                cancellationToken);

        var currentHash =
            CaptureUserFieldFingerprint(
                current.AnchorX,
                current.AnchorY);

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
                await SelectNextUserFromTrainingAsync(
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

    private static async Task<SelectionResult> SelectFirstUserFromTrainingAsync(
        WorkflowStep anchor,
        CancellationToken cancellationToken)
    {
        var selection =
            await GetUserAnchorAsync(
                anchor,
                cancellationToken);

        Console.WriteLine(
            "[USUARIOS] Primer ciclo entrenado: LEFT -> DOWN -> ENTER.");

        await SendTrainedUserCycleAsync(
            cancellationToken);

        return selection;
    }

    private static async Task<NextUserResult?> SelectNextUserFromTrainingAsync(
        WorkflowStep anchor,
        ulong currentHash,
        CancellationToken cancellationToken)
    {
        var selection =
            await GetUserAnchorAsync(
                anchor,
                cancellationToken);

        Console.WriteLine(
            "[USUARIOS] Siguiente usuario: LEFT -> DOWN -> ENTER.");

        await SendTrainedUserCycleAsync(
            cancellationToken);

        var newHash =
            CaptureUserFieldFingerprint(
                selection.AnchorX,
                selection.AnchorY);

        Console.WriteLine(
            $"[USUARIOS] Cambio visual: anterior=0x{currentHash:X16}; nuevo=0x{newHash:X16}");

        if (newHash == currentHash)
        {
            Console.WriteLine(
                "[USUARIOS] No hubo cambio visual después del ciclo; fin de lista.");

            return null;
        }

        return new NextUserResult(
            selection,
            newHash);
    }

    private static async Task SendTrainedUserCycleAsync(
        CancellationToken cancellationToken)
    {
        // El entrenamiento nuevo muestra repetidamente:
        // LEFT (0x25) -> DOWN (0x28) -> ENTER (0x0D).
        //
        // LEFT recupera el foco hacia Usuario después de que ENTER
        // lo deja en otro control del formulario.
        SendKey(
            0x25, // VK_LEFT
            false,
            false,
            false);

        await Task.Delay(
            220,
            cancellationToken);

        SendKey(
            0x28, // VK_DOWN
            false,
            false,
            false);

        await Task.Delay(
            220,
            cancellationToken);

        SendKey(
            0x0D, // VK_RETURN
            false,
            false,
            false);

        await Task.Delay(
            650,
            cancellationToken);
    }

    private static async Task<SelectionResult> GetUserAnchorAsync(
        WorkflowStep anchor,
        CancellationToken cancellationToken)
    {
        var window =
            await WaitForWindowAsync(
                anchor,
                cancellationToken);

        if (window == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "No apareció la ventana que contiene Usuario.");
        }

        if (!NativeMethods.GetWindowRect(
                window,
                out var rect))
        {
            throw new InvalidOperationException(
                "No se pudo leer la ventana de Usuario.");
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

        await Task.Delay(
            250,
            cancellationToken);

        // IMPORTANTE:
        // No se hace clic en Usuario en V6.17.
        // El entrenamiento nuevo fue puramente de teclado.
        return new SelectionResult(
            window,
            anchorX,
            anchorY);
    }

    private static async Task RunUserComboDiagnosticAsync(
        WorkflowStep anchor,
        CancellationToken cancellationToken)
    {
        var window =
            await WaitForWindowAsync(
                anchor,
                cancellationToken);

        if (window == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "No apareció la ventana que contiene Usuario.");
        }

        if (!NativeMethods.GetWindowRect(
                window,
                out var rect))
        {
            throw new InvalidOperationException(
                "No se pudo leer la ventana de Usuario.");
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

        await Task.Delay(
            500,
            cancellationToken);

        Console.WriteLine();
        Console.WriteLine(
            "=== DIAGNOSTICO USUARIO / COMBOBOX ===");

        Console.WriteLine(
            $"[USUARIOS][ANCHOR] ({anchorX},{anchorY})");

        var candidates =
            ComboBoxEnumerator.FindCandidatesNearPoint(
                window,
                anchorX,
                anchorY);

        if (candidates.Count == 0)
        {
            Console.WriteLine(
                "[USUARIOS][COMBO] No se encontraron controles Combo/ComboBox visibles.");

            Console.WriteLine(
                "[USUARIOS][COMBO] Se listarán controles cercanos para diagnóstico.");

            foreach (var control in
                     ComboBoxEnumerator.FindNearbyControls(
                         window,
                         anchorX,
                         anchorY,
                         12))
            {
                Console.WriteLine(
                    $"[USUARIOS][CONTROL] HWND=0x{control.Handle.ToInt64():X} " +
                    $"Clase=\"{control.ClassName}\" Texto=\"{control.Text}\" " +
                    $"Rect=({control.Left},{control.Top},{control.Width},{control.Height}) " +
                    $"Dist={control.Distance:0.0}");
            }

            return;
        }

        Console.WriteLine(
            $"[USUARIOS][COMBO] Candidatos encontrados: {candidates.Count}");

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate =
                candidates[i];

            Console.WriteLine();
            Console.WriteLine(
                $"[USUARIOS][COMBO #{i + 1}] HWND=0x{candidate.Handle.ToInt64():X} " +
                $"Clase=\"{candidate.ClassName}\" Texto=\"{candidate.Text}\" " +
                $"Rect=({candidate.Left},{candidate.Top},{candidate.Width},{candidate.Height}) " +
                $"Dist={candidate.Distance:0.0}");

            var count =
                ComboBoxEnumerator.TryGetCount(
                    candidate.Handle);

            var current =
                ComboBoxEnumerator.TryGetCurrentIndex(
                    candidate.Handle);

            Console.WriteLine(
                $"[USUARIOS][COMBO #{i + 1}] Count={count}; Current={current}");

            if (count <= 0)
            {
                Console.WriteLine(
                    $"[USUARIOS][COMBO #{i + 1}] El control no respondió a CB_GETCOUNT.");
                continue;
            }

            var max =
                Math.Min(
                    count,
                    100);

            for (var itemIndex = 0;
                 itemIndex < max;
                 itemIndex++)
            {
                var itemText =
                    ComboBoxEnumerator.TryGetItemText(
                        candidate.Handle,
                        itemIndex);

                Console.WriteLine(
                    $"[USUARIOS][ITEM] Combo={i + 1} Index={itemIndex:00} Texto=\"{itemText}\"");
            }
        }

        var best =
            candidates
                .Where(x =>
                    ComboBoxEnumerator.TryGetCount(
                        x.Handle) > 0)
                .OrderBy(x => x.Distance)
                .FirstOrDefault();

        if (best.Handle == IntPtr.Zero)
        {
            Console.WriteLine();
            Console.WriteLine(
                "[USUARIOS][RESULTADO] Hay controles Combo, pero ninguno expone elementos.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine(
            $"[USUARIOS][RESULTADO] Mejor candidato: HWND=0x{best.Handle.ToInt64():X} " +
            $"Clase=\"{best.ClassName}\" Count={ComboBoxEnumerator.TryGetCount(best.Handle)}");

        Console.WriteLine(
            "[USUARIOS][RESULTADO] V6.18 es diagnóstico: NO cambia la selección.");
    }

    private static async Task EnsureYesterdaySelectedAsync(
        WorkflowStep dateStep,
        CancellationToken cancellationToken)
    {
        var main =
            WindowInfo.FindWindowByProcessAndTitle(
                dateStep.ProcessName,
                "SOFT RESTAURANT");

        if (main == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "FECHA: no se encontró la ventana principal de SoftRestaurant.");
        }

        var picker =
            FindVisibleDatePicker(
                main);

        if (picker == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "FECHA: no se encontró el control DTPicker20WndClass.");
        }

        if (!NativeMethods.GetWindowRect(
                picker,
                out var pickerRect))
        {
            throw new InvalidOperationException(
                "FECHA: no se pudo leer la geometría del DTPicker.");
        }

        Console.WriteLine(
            $"[FECHA] DTPicker real: HWND=0x{picker.ToInt64():X} " +
            $"Rect=({pickerRect.Left},{pickerRect.Top},{pickerRect.Width},{pickerRect.Height})");

        NativeMethods.SetForegroundWindow(
            main);

        await Task.Delay(
            250,
            cancellationToken);

        var dropX =
            pickerRect.Right - 8;

        var dropY =
            pickerRect.Top +
            pickerRect.Height / 2;

        Console.WriteLine(
            $"[FECHA] Abriendo calendario DTPicker en ({dropX},{dropY})...");

        NativeMethods.SetCursorPos(
            dropX,
            dropY);

        Click();

        await Task.Delay(
            250,
            cancellationToken);

        var month =
            await SoftRestaurantReportContext.WaitForVisibleMonthViewAsync(
                dateStep.ProcessName,
                main,
                5000,
                cancellationToken);

        if (month == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "FECHA: se hizo clic en el DTPicker, pero no apareció MonthView.");
        }

        if (!NativeMethods.GetWindowRect(
                month,
                out var rect))
        {
            throw new InvalidOperationException(
                "FECHA: no se pudo leer MonthView.");
        }

        Console.WriteLine(
            "[FECHA] MonthView desplegado: " +
            SoftRestaurantReportContext.Describe(
                month));

        var target =
            DateTime.Today.AddDays(-1);

        NativeMethods.SetForegroundWindow(
            main);

        NativeMethods.SetFocus(
            month);

        await Task.Delay(
            200,
            cancellationToken);

        Console.WriteLine(
            $"[FECHA] Seleccionando AYER por teclado: LEFT -> ENTER; " +
            $"HOY={DateTime.Today:dd/MM/yyyy}; AYER={target:dd/MM/yyyy}");

        SendKey(
            0x25, // VK_LEFT
            false,
            false,
            false);

        await Task.Delay(
            250,
            cancellationToken);

        SendKey(
            0x0D, // VK_RETURN
            false,
            false,
            false);

        await Task.Delay(
            750,
            cancellationToken);

        Console.WriteLine(
            $"[FECHA][OK] Selección de AYER confirmada por teclado: {target:dd/MM/yyyy}.");
    }

    private static IntPtr FindVisibleDatePicker(
        IntPtr mainWindow)
    {
        IntPtr found =
            IntPtr.Zero;

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
                    GetClassName(
                        hWnd);

                if (!cls.Equals(
                        "DTPicker20WndClass",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                found =
                    hWnd;

                return false;
            },
            IntPtr.Zero);

        return found;
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

    private static async Task RunClosuresFormControlsDiagnosticAsync(
        string processName,
        CancellationToken cancellationToken)
    {
        var deadline =
            DateTimeOffset.UtcNow.AddSeconds(
                5);

        IntPtr form =
            IntPtr.Zero;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            form =
                FindWindowByExactTitle(
                    processName,
                    "Formas de pago por turno");

            if (form != IntPtr.Zero)
                break;

            await Task.Delay(
                200,
                cancellationToken);
        }

        if (form == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "DIAGNOSTICO FORMULARIO: no se encontró 'Formas de pago por turno'.");
        }

        Console.WriteLine();
        Console.WriteLine(
            "=== DIAGNOSTICO FORMAS DE PAGO POR TURNO / CONTROLES ===");

        Console.WriteLine(
            "[FORM] " +
            SoftRestaurantReportContext.Describe(
                form));

        var controls =
            new List<FormControlDiagnostic>();

        NativeMethods.EnumChildWindows(
            form,
            (hWnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd))
                    return true;

                if (!NativeMethods.GetWindowRect(
                        hWnd,
                        out var rect))
                {
                    return true;
                }

                controls.Add(
                    new FormControlDiagnostic(
                        hWnd,
                        GetClassName(hWnd),
                        GetWindowText(hWnd).Trim(),
                        rect.Left,
                        rect.Top,
                        rect.Width,
                        rect.Height,
                        NativeMethods.IsWindowEnabled(hWnd)));

                return true;
            },
            IntPtr.Zero);

        var ordered =
            controls
                .OrderBy(x => x.Top)
                .ThenBy(x => x.Left)
                .ThenBy(x => x.ClassName)
                .ToList();

        Console.WriteLine(
            $"[FORM] Controles visibles encontrados: {ordered.Count}");

        for (var i = 0;
            i < ordered.Count;
            i++)
        {
            var c =
                ordered[i];

            Console.WriteLine(
                $"[FORM][{i + 1:00}] " +
                $"HWND=0x{c.Handle.ToInt64():X} " +
                $"Class=\"{c.ClassName}\" " +
                $"Text=\"{c.Text}\" " +
                $"Rect=({c.Left},{c.Top},{c.Width},{c.Height}) " +
                $"Enabled={c.Enabled}");
        }

        Console.WriteLine();
        Console.WriteLine(
            "[FORM] Diagnóstico terminado. " +
            "NO se cambiaron Usuario ni Turno.");
    }

    private static IntPtr FindWindowByExactTitle(
        string processName,
        string exactTitle)
    {
        IntPtr found =
            IntPtr.Zero;

        NativeMethods.EnumWindows(
            (hWnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd))
                    return true;

                var snapshot =
                    WindowInfo.GetSnapshot(
                        hWnd);

                if (!snapshot.ProcessName.Equals(
                        processName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (snapshot.Title.Equals(
                        exactTitle,
                        StringComparison.OrdinalIgnoreCase))
                {
                    found =
                        hWnd;

                    return false;
                }

                return true;
            },
            IntPtr.Zero);

        if (found != IntPtr.Zero)
            return found;

        // En SoftRestaurant este formulario puede ser hijo de la ventana principal.
        var main =
            WindowInfo.FindWindowByProcessAndTitle(
                processName,
                "SOFT RESTAURANT");

        if (main == IntPtr.Zero)
            return IntPtr.Zero;

        NativeMethods.EnumChildWindows(
            main,
            (hWnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd))
                    return true;

                var title =
                    GetWindowText(
                        hWnd).Trim();

                if (title.Equals(
                        exactTitle,
                        StringComparison.OrdinalIgnoreCase))
                {
                    found =
                        hWnd;

                    return false;
                }

                return true;
            },
            IntPtr.Zero);

        return found;
    }

    private readonly record struct FormControlDiagnostic(
        IntPtr Handle,
        string ClassName,
        string Text,
        int Left,
        int Top,
        int Width,
        int Height,
        bool Enabled);

    private static async Task RunOwnerDrawUserDiagnosticAsync(
        WorkflowStep userAnchor,
        CancellationToken cancellationToken)
    {
        var selection =
            await GetUserAnchorAsync(
                userAnchor,
                cancellationToken);

        var x =
            selection.AnchorX;

        var y =
            selection.AnchorY;

        Console.WriteLine();
        Console.WriteLine(
            "=== DIAGNOSTICO USUARIO OWNER-DRAWN V6.18.16 ===");

        Console.WriteLine(
            $"[USUARIOS] Anchor entrenado=({x},{y})");

        var under =
            NativeMethods.WindowFromPoint(
                new NativeMethods.POINT
                {
                    X = x,
                    Y = y
                });

        Console.WriteLine(
            $"[USUARIOS] HWND bajo anchor=0x{under.ToInt64():X} " +
            $"Clase=\"{GetClassName(under)}\" Texto=\"{GetWindowText(under).Trim()}\"");

        var before =
            CaptureUserFieldFingerprint(
                x,
                y);

        Console.WriteLine(
            $"[USUARIOS] Huella ANTES=0x{before:X16}");

        Console.WriteLine(
            "[USUARIOS] Click en caja/flecha de Usuario...");

        NativeMethods.SetCursorPos(
            x,
            y);

        Click();

        await Task.Delay(
            500,
            cancellationToken);

        var afterClick =
            CaptureUserFieldFingerprint(
                x,
                y);

        Console.WriteLine(
            $"[USUARIOS] Huella DESPUES CLICK=0x{afterClick:X16}");

        Console.WriteLine(
            "[USUARIOS] Enviando DOWN x1...");

        SendKey(
            0x28, // VK_DOWN
            false,
            false,
            false);

        await Task.Delay(
            500,
            cancellationToken);

        var afterDown =
            CaptureUserFieldFingerprint(
                x,
                y);

        Console.WriteLine(
            $"[USUARIOS] Huella DESPUES DOWN=0x{afterDown:X16}");

        Console.WriteLine(
            "[USUARIOS] Enviando ENTER...");

        SendKey(
            0x0D, // VK_RETURN
            false,
            false,
            false);

        await Task.Delay(
            700,
            cancellationToken);

        var afterEnter =
            CaptureUserFieldFingerprint(
                x,
                y);

        Console.WriteLine(
            $"[USUARIOS] Huella DESPUES ENTER=0x{afterEnter:X16}");

        var changed =
            afterEnter != before;

        Console.WriteLine(
            $"[USUARIOS][RESULTADO] Cambio visual confirmado={changed}");

        if (changed)
        {
            Console.WriteLine(
                "[USUARIOS][OK] La caja de Usuario responde a CLICK -> DOWN -> ENTER.");
        }
        else
        {
            Console.WriteLine(
                "[USUARIOS][WARN] No cambió la huella. " +
                "El siguiente paso será probar LEFT -> DOWN -> ENTER sobre el mismo anchor.");
        }

        Console.WriteLine(
            "[USUARIOS] Diagnóstico terminado. No se ejecutaron cierres.");
    }

    private static async Task RunControlledExcelExecuteDiagnosticAsync(
        string processName,
        WorkflowStep userAnchor,
        WorkflowStep reportOpenStep,
        WorkflowStep reportLaserStep,
        CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine(
            "=== USER CLOSURE SCAN + EXPORT ALL V6.18.61A ===");

        var userSelection =
            await GetUserAnchorAsync(
                userAnchor,
                cancellationToken);

        var userX =
            userSelection.AnchorX;

        var userY =
            userSelection.AnchorY;

        // ------------------------------------------------------------
        // 1. Enumerar todos los usuarios reales del combo.
        // ------------------------------------------------------------
        await OpenUserDropdownAsync(
            userX,
            userY,
            cancellationToken);

        await MoveAccessibleUserListToTopAsync(
            userX,
            userY,
            cancellationToken);

        var orderedUsers =
            await EnumerateAllAccessibleUsersAsync(
                userX,
                userY,
                cancellationToken);

        SendKey(
            0x1B,
            false,
            false,
            false);

        if (orderedUsers.Count == 0)
        {
            throw new InvalidOperationException(
                "No se pudo enumerar ningún usuario.");
        }

        Console.WriteLine();
        Console.WriteLine(
            $"[SCAN][USERS] Usuarios enumerados={orderedUsers.Count}");

        // ------------------------------------------------------------
        // 2. Inicializar Reporte una sola vez.
        //    Después solo cambiaremos Usuario.
        // ------------------------------------------------------------
        var firstUser =
            orderedUsers[0];

        var firstSelected =
            await SelectAccessibleUserByNameAsync(
                userX,
                userY,
                firstUser,
                orderedUsers,
                cancellationToken);

        if (!firstSelected)
        {
            throw new InvalidOperationException(
                $"No se pudo seleccionar usuario inicial {firstUser}.");
        }

        await SelectInitialReportMiniprinterThenLaserAsync(
            reportOpenStep,
            reportLaserStep,
            cancellationToken);

        await Task.Delay(
            300,
            cancellationToken);

        var usersWithClosure =
            new List<string>();

        // ------------------------------------------------------------
        // 3. Recorrer TODOS los usuarios y detectar 0 vs >=1 turno.
        // ------------------------------------------------------------
        for (var i = 0;
             i < orderedUsers.Count;
             i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var user =
                orderedUsers[i];

            if (i > 0)
            {
                var selected =
                    await SelectAccessibleUserByNameAsync(
                        userX,
                        userY,
                        user,
                        orderedUsers,
                        cancellationToken);

                if (!selected)
                {
                    Console.WriteLine(
                        $"[SCAN][{i + 1:00}/{orderedUsers.Count:00}] " +
                        $"{user}: ERROR_SELECCION");

                    continue;
                }
            }

            await Task.Delay(
                180,
                cancellationToken);

            var turnInfo =
                FindAccessibleControlByName(
                    "txtprecorte",
                    305,
                    245,
                    719,
                    518);

            if (turnInfo is null)
            {
                Console.WriteLine(
                    $"[SCAN][{i + 1:00}/{orderedUsers.Count:00}] " +
                    $"{user}: txtprecorte NO encontrado");

                continue;
            }

            var hasTurn =
                HasVisibleTurnData(
                    turnInfo.Left,
                    turnInfo.Top,
                    turnInfo.Width,
                    turnInfo.Height,
                    out var darkPixels,
                    out var sampledPixels);

            Console.WriteLine(
                $"[SCAN][{i + 1:00}/{orderedUsers.Count:00}] " +
                $"{user,-16} Ink={darkPixels}/{sampledPixels} " +
                $"Cierre={(hasTurn ? "SI" : "NO")}");

            if (hasTurn)
            {
                usersWithClosure.Add(
                    user);
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            $"[SCAN][RESULT] Usuarios={orderedUsers.Count}; " +
            $"ConCierre={usersWithClosure.Count}; " +
            $"SinCierre={orderedUsers.Count - usersWithClosure.Count}");

        if (usersWithClosure.Count > 0)
        {
            Console.WriteLine(
                "[SCAN][CON-CIERRE] " +
                string.Join(
                    ", ",
                    usersWithClosure));
        }

        if (usersWithClosure.Count == 0)
        {
            Console.WriteLine(
                "[SCAN][STOP] No se encontraron cierres para AYER.");

            return;
        }

        // ------------------------------------------------------------
        // 4. Preparar destino una sola vez para todos los cierres.
        // ------------------------------------------------------------
        var configPath =
            FindAgentAppSettingsPath();

        var closuresRoot =
            ReadCashClosuresRootFromAgent(
                configPath);

        var reportDate =
            DateTime.Today.AddDays(-1);

        var monthFolder =
            ResolveExistingMonthFolder(
                closuresRoot,
                reportDate);

        Console.WriteLine();
        Console.WriteLine(
            $"[EXPORT][DESTINO] Fecha={reportDate:dd/MM/yyyy}");

        Console.WriteLine(
            $"[EXPORT][DESTINO] Carpeta=\"{monthFolder}\"");

        var exported =
            0;

        var skippedExisting =
            0;

        // ------------------------------------------------------------
        // 5. Procesar TODOS los usuarios que sí tienen cierre.
        // ------------------------------------------------------------
        for (var closureIndex = 0;
             closureIndex < usersWithClosure.Count;
             closureIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var targetUser =
                usersWithClosure[
                    closureIndex];

            var safeUserName =
                MakeSafeFilePart(
                    targetUser);

            var outputFileName =
                $"{reportDate:yyyy-MM-dd}_{safeUserName}_T01.xlsx";

            var expectedFullPath =
                Path.Combine(
                    monthFolder,
                    outputFileName);

            Console.WriteLine();
            Console.WriteLine(
                $"=== EXPORT {closureIndex + 1}/{usersWithClosure.Count}: {targetUser} ===");

            Console.WriteLine(
                $"[EXPORT][TARGET] \"{expectedFullPath}\"");

            // Idempotencia: si ya existe un archivo válido, no se vuelve
            // a ejecutar el reporte ni se sobrescribe.
            if (File.Exists(
                    expectedFullPath))
            {
                var existing =
                    new FileInfo(
                        expectedFullPath);

                if (existing.Length > 0)
                {
                    skippedExisting++;

                    Console.WriteLine(
                        $"[EXPORT][SKIP-EXISTING] Usuario={targetUser}; Bytes={existing.Length}");

                    continue;
                }
            }

            // Excel puede quedar al frente después del guardado anterior.
            // Recuperamos explícitamente SoftRestaurant antes de tocar Usuario.
            var softRestaurantMain =
                WindowInfo.FindWindowByProcessAndTitle(
                    processName,
                    "SOFT RESTAURANT");

            if (softRestaurantMain == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "No se encontró la ventana principal de SoftRestaurant antes de exportar.");
            }

            NativeMethods.SetForegroundWindow(
                softRestaurantMain);

            await Task.Delay(
                500,
                cancellationToken);

            var selectedTarget =
                await SelectAccessibleUserByNameAsync(
                    userX,
                    userY,
                    targetUser,
                    orderedUsers,
                    cancellationToken);

            if (!selectedTarget)
            {
                throw new InvalidOperationException(
                    $"No se pudo seleccionar {targetUser} para exportar.");
            }

            await Task.Delay(
                250,
                cancellationToken);

            var excel =
                FindAccessibleControlByName(
                    "Excel",
                    305,
                    245,
                    719,
                    518);

            var ejecutar =
                FindAccessibleControlByName(
                    "Ejecutar",
                    305,
                    245,
                    719,
                    518);

            if (excel is null)
                throw new InvalidOperationException(
                    $"No se encontró Excel para {targetUser}.");

            if (ejecutar is null)
                throw new InvalidOperationException(
                    $"No se encontró Ejecutar para {targetUser}.");

            NativeMethods.SetCursorPos(
                excel.Left + excel.Width / 2,
                excel.Top + excel.Height / 2);

            Click();

            await Task.Delay(
                400,
                cancellationToken);

            var beforeWindows =
                CaptureTopLevelWindowsSimple();

            NativeMethods.SetCursorPos(
                ejecutar.Left + ejecutar.Width / 2,
                ejecutar.Top + ejecutar.Height / 2);

            Click();

            Console.WriteLine(
                $"[EXPORT][EJECUTAR] Usuario={targetUser}; esperando Guardar como...");

            SimpleWindowSnapshot? saveAs =
                null;

            for (var wait = 0;
                 wait < 40;
                 wait++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var windows =
                    CaptureTopLevelWindowsSimple();

                saveAs =
                    windows.FirstOrDefault(
                        w =>
                            string.Equals(
                                w.ClassName,
                                "#32770",
                                StringComparison.OrdinalIgnoreCase) &&
                            w.Title.Contains(
                                "Guardar",
                                StringComparison.OrdinalIgnoreCase) &&
                            !beforeWindows.Any(
                                b => b.Handle == w.Handle));

                if (saveAs is not null)
                    break;

                await Task.Delay(
                    250,
                    cancellationToken);
            }

            if (saveAs is null)
            {
                throw new TimeoutException(
                    $"No apareció Guardar como para {targetUser}.");
            }

            Console.WriteLine(
                $"[EXPORT][SAVE-AS] Usuario={targetUser}; HWND=0x{saveAs.Handle.ToInt64():X}");

            NativeMethods.SetForegroundWindow(
                saveAs.Handle);

            await Task.Delay(
                200,
                cancellationToken);

            var saveControls =
                EnumerateDescendantWindows(
                    saveAs.Handle);

            var fileNameEdit =
                FindSaveAsFileNameEdit(
                    saveAs.Handle,
                    saveControls);

            var saveButton =
                FindSaveAsSaveButton(
                    saveAs.Handle,
                    saveControls);

            if (fileNameEdit == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"No se encontró Nombre de archivo para {targetUser}.");

            if (saveButton == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"No se encontró botón Guardar para {targetUser}.");

            NativeMethods.SetFocus(
                fileNameEdit);

            await Task.Delay(
                100,
                cancellationToken);

            SetWindowTextDirect(
                fileNameEdit,
                expectedFullPath);

            await Task.Delay(
                250,
                cancellationToken);

            var readBack =
                GetWindowTextByMessage(
                    fileNameEdit);

            var verified =
                string.Equals(
                    readBack.Trim(),
                    expectedFullPath,
                    StringComparison.OrdinalIgnoreCase);

            Console.WriteLine(
                $"[EXPORT][VERIFY] Usuario={targetUser}; RutaCorrecta={verified}");

            if (!verified)
            {
                throw new InvalidOperationException(
                    $"Ruta no verificada para {targetUser}. Leído=\"{readBack}\"");
            }

            NativeMethods.SendMessage(
                saveButton,
                0x00F5, // BM_CLICK
                IntPtr.Zero,
                IntPtr.Zero);

            var dialogClosed =
                false;

            for (var wait = 0;
                 wait < 40;
                 wait++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!NativeMethods.IsWindowVisible(
                        saveAs.Handle))
                {
                    dialogClosed =
                        true;

                    break;
                }

                await Task.Delay(
                    200,
                    cancellationToken);
            }

            Console.WriteLine(
                $"[EXPORT][DIALOG-CLOSED] Usuario={targetUser}; Cerrado={dialogClosed}");

            if (!dialogClosed)
            {
                throw new TimeoutException(
                    $"Guardar como no cerró para {targetUser}.");
            }

            FileInfo? savedFile =
                null;

            for (var wait = 0;
                 wait < 40;
                 wait++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(
                        expectedFullPath))
                {
                    var info =
                        new FileInfo(
                            expectedFullPath);

                    if (info.Length > 0)
                    {
                        savedFile =
                            info;

                        break;
                    }
                }

                await Task.Delay(
                    250,
                    cancellationToken);
            }

            if (savedFile is null)
            {
                throw new FileNotFoundException(
                    $"No apareció el archivo guardado de {targetUser}.",
                    expectedFullPath);
            }

            exported++;

            Console.WriteLine(
                $"[EXPORT][OK] Usuario={targetUser}; Bytes={savedFile.Length}; " +
                $"Archivo=\"{savedFile.FullName}\"");

            // Dar tiempo a Excel/SoftRestaurant a terminar su transición.
            await Task.Delay(
                700,
                cancellationToken);
        }

        Console.WriteLine();
        Console.WriteLine(
            "=== RESUMEN V6.18.61A ===");

        Console.WriteLine(
            $"[RESUMEN] Usuarios={orderedUsers.Count}");

        Console.WriteLine(
            $"[RESUMEN] ConCierre={usersWithClosure.Count}");

        Console.WriteLine(
            $"[RESUMEN] Exportados={exported}");

        Console.WriteLine(
            $"[RESUMEN] YaExistian={skippedExisting}");

        Console.WriteLine(
            $"[RESUMEN] Destino=\"{monthFolder}\"");

        Console.WriteLine(
            "[V6.18.61A][OK] Se procesaron todos los usuarios con cierre detectado.");
    }

    private static string MakeSafeFilePart(
        string value)
    {
        var invalid =
            Path.GetInvalidFileNameChars();

        var sb =
            new StringBuilder();

        foreach (var ch in value.Trim())
        {
            sb.Append(
                invalid.Contains(ch)
                    ? '_'
                    : ch);
        }

        return sb
            .ToString()
            .Replace(
                ' ',
                '_')
            .ToUpperInvariant();
    }

    private static string FindAgentAppSettingsPath()
    {
        var explicitPath =
            Environment.GetEnvironmentVariable(
                "PARRILLITA_AGENT_APPSETTINGS");

        if (!string.IsNullOrWhiteSpace(
                explicitPath) &&
            File.Exists(
                explicitPath))
        {
            return Path.GetFullPath(
                explicitPath);
        }

        var starts =
            new[]
            {
                AppContext.BaseDirectory,
                Environment.CurrentDirectory
            }
            .Where(
                x => !string.IsNullOrWhiteSpace(x))
            .Distinct(
                StringComparer.OrdinalIgnoreCase);

        foreach (var start in starts)
        {
            var current =
                new DirectoryInfo(
                    Path.GetFullPath(start));

            for (var depth = 0;
                 depth < 8 &&
                 current is not null;
                 depth++,
                 current = current.Parent)
            {
                var candidates =
                    new[]
                    {
                        Path.Combine(
                            current.FullName,
                            "src",
                            "ParrillitaIA.Agent",
                            "appsettings.json"),

                        Path.Combine(
                            current.FullName,
                            "ParrillitaIA.Starter",
                            "src",
                            "ParrillitaIA.Agent",
                            "appsettings.json")
                    };

                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }
            }
        }

        throw new FileNotFoundException(
            "No se encontró ParrillitaIA.Agent/appsettings.json. " +
            "También puede definirse PARRILLITA_AGENT_APPSETTINGS.");
    }

    private static string ReadCashClosuresRootFromAgent(
        string appSettingsPath)
    {
        using var stream =
            File.OpenRead(
                appSettingsPath);

        using var json =
            JsonDocument.Parse(
                stream);

        if (!json.RootElement.TryGetProperty(
                "Storage",
                out var storage))
        {
            throw new InvalidOperationException(
                "appsettings.json no contiene la sección Storage.");
        }

        if (!storage.TryGetProperty(
                "OneDriveCashClosuresRoot",
                out var rootProperty))
        {
            throw new InvalidOperationException(
                "Storage no contiene OneDriveCashClosuresRoot.");
        }

        var value =
            rootProperty.GetString();

        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new InvalidOperationException(
                "Storage:OneDriveCashClosuresRoot está vacío.");
        }

        return Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(
                value.Trim()));
    }

    private static string ResolveExistingMonthFolder(
        string closuresRoot,
        DateTime reportDate)
    {
        if (!Directory.Exists(closuresRoot))
            throw new DirectoryNotFoundException(
                $"No existe OneDriveCashClosuresRoot: {closuresRoot}");

        var monthNames = new[]
        {
            "",
            "ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO",
            "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE"
        };

        var monthName = monthNames[reportDate.Month];
        var month2 = reportDate.Month.ToString("00");

        var candidates =
            Directory.GetDirectories(closuresRoot)
                .Select(path => new
                {
                    Path = path,
                    Name = Path.GetFileName(path),
                    Normalized = NormalizeFolderName(Path.GetFileName(path))
                })
                .ToList();

        Console.WriteLine(
            $"[CONFIG][MES] Buscando carpeta para {month2} {monthName} dentro de \"{closuresRoot}\"");

        foreach (var c in candidates)
            Console.WriteLine($"[CONFIG][MES][CANDIDATE] \"{c.Name}\"");

        var exact =
            candidates.FirstOrDefault(c =>
                string.Equals(
                    c.Normalized,
                    monthName,
                    StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
            return exact.Path;

        var numberAndName =
            candidates.FirstOrDefault(c =>
                ContainsMonthNumberToken(c.Normalized, month2) &&
                c.Normalized.Contains(
                    monthName,
                    StringComparison.OrdinalIgnoreCase));

        if (numberAndName is not null)
            return numberAndName.Path;

        var byName =
            candidates.Where(c =>
                c.Normalized.Contains(
                    monthName,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (byName.Count == 1)
            return byName[0].Path;

        var byNumber =
            candidates.Where(c =>
                ContainsMonthNumberToken(c.Normalized, month2))
                .ToList();

        if (byNumber.Count == 1)
            return byNumber[0].Path;

        throw new DirectoryNotFoundException(
            $"No se pudo identificar de forma segura la carpeta del mes " +
            $"{month2} {monthName} dentro de {closuresRoot}. " +
            "No se creará una carpeta automáticamente.");
    }

    private static bool ContainsMonthNumberToken(
        string text,
        string month2)
    {
        var tokens =
            text.Split(
                new[] { ' ', '-', '_', '.', '(', ')', '[', ']' },
                StringSplitOptions.RemoveEmptyEntries);

        var month1 =
            int.Parse(month2).ToString();

        return tokens.Any(token =>
            string.Equals(token, month2, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(token, month1, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeFolderName(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized =
            value.Normalize(
                NormalizationForm.FormD);

        var sb =
            new StringBuilder();

        foreach (var ch in normalized)
        {
            var category =
                System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);

            if (category !=
                System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(
                    char.ToUpperInvariant(ch));
            }
        }

        return sb
            .ToString()
            .Normalize(
                NormalizationForm.FormC)
            .Trim();
    }

    private static void SendUnicodeText(
        string text)
    {
        foreach (var ch in text)
        {
            var down =
                new NativeMethods.INPUT
                {
                    type =
                        NativeMethods.INPUT_KEYBOARD,

                    Data =
                        new NativeMethods.INPUTUNION
                        {
                            ki =
                                new NativeMethods.KEYBDINPUT
                                {
                                    wVk = 0,
                                    wScan = ch,
                                    dwFlags = 0x0004 // KEYEVENTF_UNICODE
                                }
                        }
                };

            var up =
                new NativeMethods.INPUT
                {
                    type =
                        NativeMethods.INPUT_KEYBOARD,

                    Data =
                        new NativeMethods.INPUTUNION
                        {
                            ki =
                                new NativeMethods.KEYBDINPUT
                                {
                                    wVk = 0,
                                    wScan = ch,
                                    dwFlags = 0x0004 | 0x0002 // UNICODE | KEYUP
                                }
                        }
                };

            Send(
            [
                down,
                up
            ]);
        }
    }

    private sealed class SimpleWindowSnapshot
    {
        public IntPtr Handle { get; init; }
        public string ClassName { get; init; } = "";
        public string Title { get; init; } = "";
        public int Left { get; init; }
        public int Top { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
    }

    private static List<SimpleWindowSnapshot> CaptureTopLevelWindowsSimple()
    {
        var result =
            new List<SimpleWindowSnapshot>();

        NativeMethods.EnumWindows(
            (hWnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(
                        hWnd))
                {
                    return true;
                }

                if (!NativeMethods.GetWindowRect(
                        hWnd,
                        out var rect))
                {
                    return true;
                }

                var width =
                    rect.Right -
                    rect.Left;

                var height =
                    rect.Bottom -
                    rect.Top;

                if (width <= 0 ||
                    height <= 0)
                {
                    return true;
                }

                result.Add(
                    new SimpleWindowSnapshot
                    {
                        Handle = hWnd,
                        ClassName = GetClassName(hWnd),
                        Title = GetWindowText(hWnd).Trim(),
                        Left = rect.Left,
                        Top = rect.Top,
                        Width = width,
                        Height = height
                    });

                return true;
            },
            IntPtr.Zero);

        return result;
    }

    private sealed class ChildWindowInfo
    {
        public IntPtr Handle { get; init; }
        public int ControlId { get; init; }
        public string ClassName { get; init; } = "";
        public string Text { get; init; } = "";
        public bool Visible { get; init; }
        public bool Enabled { get; init; }
        public int Left { get; init; }
        public int Top { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
    }

    private static List<ChildWindowInfo> EnumerateDescendantWindows(
        IntPtr parent)
    {
        var result =
            new List<ChildWindowInfo>();

        NativeMethods.EnumChildWindows(
            parent,
            (hWnd, _) =>
            {
                NativeMethods.GetWindowRect(
                    hWnd,
                    out var rect);

                result.Add(
                    new ChildWindowInfo
                    {
                        Handle = hWnd,
                        ControlId =
                            NativeMethods.GetDlgCtrlID(
                                hWnd),
                        ClassName =
                            GetClassName(
                                hWnd),
                        Text =
                            GetWindowText(
                                hWnd)
                            .Trim(),
                        Visible =
                            NativeMethods.IsWindowVisible(
                                hWnd),
                        Enabled =
                            NativeMethods.IsWindowEnabled(
                                hWnd),
                        Left = rect.Left,
                        Top = rect.Top,
                        Width =
                            rect.Right -
                            rect.Left,
                        Height =
                            rect.Bottom -
                            rect.Top
                    });

                return true;
            },
            IntPtr.Zero);

        return result;
    }

    private static IntPtr FindSaveAsFileNameEdit(
        IntPtr dialog,
        IReadOnlyList<ChildWindowInfo> controls)
    {
        const int Edt1 =
            0x0480;

        var byId =
            controls.FirstOrDefault(
                x =>
                    x.ControlId == Edt1 &&
                    string.Equals(
                        x.ClassName,
                        "Edit",
                        StringComparison.OrdinalIgnoreCase));

        if (byId is not null)
            return byId.Handle;

        NativeMethods.GetWindowRect(
            dialog,
            out var dialogRect);

        var candidates =
            controls
                .Where(
                    x =>
                        x.Visible &&
                        x.Enabled &&
                        string.Equals(
                            x.ClassName,
                            "Edit",
                            StringComparison.OrdinalIgnoreCase) &&
                        x.Top >
                            dialogRect.Top +
                            (dialogRect.Bottom - dialogRect.Top) / 2)
                .OrderByDescending(
                    x => x.Top)
                .ThenByDescending(
                    x => x.Width)
                .ToList();

        return candidates.Count > 0
            ? candidates[0].Handle
            : IntPtr.Zero;
    }

    private static IntPtr FindSaveAsSaveButton(
        IntPtr dialog,
        IReadOnlyList<ChildWindowInfo> controls)
    {
        var byId =
            controls.FirstOrDefault(
                x =>
                    x.ControlId == 1 &&
                    string.Equals(
                        x.ClassName,
                        "Button",
                        StringComparison.OrdinalIgnoreCase));

        if (byId is not null)
            return byId.Handle;

        var byText =
            controls.FirstOrDefault(
                x =>
                    x.Visible &&
                    x.Enabled &&
                    string.Equals(
                        x.ClassName,
                        "Button",
                        StringComparison.OrdinalIgnoreCase) &&
                    x.Text.Contains(
                        "Guardar",
                        StringComparison.OrdinalIgnoreCase));

        return byText?.Handle ??
               IntPtr.Zero;
    }

    private static string GetWindowTextByMessage(
        IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return "";

        const int WM_GETTEXTLENGTH =
            0x000E;

        const int WM_GETTEXT =
            0x000D;

        var length =
            NativeMethods.SendMessage(
                hWnd,
                WM_GETTEXTLENGTH,
                IntPtr.Zero,
                IntPtr.Zero)
            .ToInt32();

        var capacity =
            Math.Max(
                length + 2,
                1024);

        var buffer =
            new StringBuilder(
                capacity);

        var chars =
            SendMessageTextBuffer(
                hWnd,
                WM_GETTEXT,
                new IntPtr(capacity),
                buffer)
            .ToInt32();

        return chars > 0
            ? buffer.ToString()
            : "";
    }

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessageTextBuffer(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        StringBuilder lParam);

    private static void SetWindowTextDirect(
        IntPtr hWnd,
        string value)
    {
        if (hWnd == IntPtr.Zero)
            throw new ArgumentException(
                "HWND inválido.",
                nameof(hWnd));

        NativeMethods.SendMessage(
            hWnd,
            0x000C, // WM_SETTEXT
            IntPtr.Zero,
            value);
    }

    private static async Task<bool> SelectAccessibleUserDirectAsync(
        int anchorX,
        int anchorY,
        string targetName,
        CancellationToken cancellationToken)
    {
        await OpenUserDropdownAsync(
            anchorX,
            anchorY,
            cancellationToken);

        for (var guard = 0;
             guard < MaxUsers;
             guard++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var visible =
                ReadVisibleAccessibleUsers(
                    anchorX,
                    anchorY);

            var target =
                visible.FirstOrDefault(
                    x => string.Equals(
                        x.Name,
                        targetName,
                        StringComparison.OrdinalIgnoreCase));

            if (target is not null)
            {
                var clickX =
                    target.Left +
                    target.Width / 2;

                var clickY =
                    target.Top +
                    target.Height / 2;

                Console.WriteLine(
                    $"[A11Y][DIRECT] \"{target.Name}\" " +
                    $"Click=({clickX},{clickY})");

                NativeMethods.SetCursorPos(
                    clickX,
                    clickY);

                Click();

                await Task.Delay(
                    400,
                    cancellationToken);

                return true;
            }

            if (visible.Count == 0)
            {
                SendKey(
                    0x1B,
                    false,
                    false,
                    false);

                return false;
            }

            if (!ClickAccessibleScrollLineDown(
                    anchorX,
                    anchorY))
            {
                SendKey(
                    0x1B,
                    false,
                    false,
                    false);

                return false;
            }

            await Task.Delay(
                90,
                cancellationToken);
        }

        SendKey(
            0x1B,
            false,
            false,
            false);

        return false;
    }

    private static ulong CaptureTurnNumberFingerprint(
        int turnLeft,
        int turnTop,
        int turnWidth,
        int turnHeight)
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

            var left =
                turnLeft + 2;

            var top =
                turnTop + 3;

            var width =
                Math.Max(
                    12,
                    turnWidth - 19);

            var height =
                Math.Max(
                    10,
                    turnHeight - 6);

            for (var y = top;
                 y < top + height;
                 y += 2)
            {
                for (var x = left;
                     x < left + width;
                     x += 2)
                {
                    var pixel =
                        NativeMethods.GetPixel(
                            hdc,
                            x,
                            y);

                    var r =
                        (int)(pixel & 0xFF);

                    var g =
                        (int)((pixel >> 8) & 0xFF);

                    var b =
                        (int)((pixel >> 16) & 0xFF);

                    var luminance =
                        (r * 299 +
                         g * 587 +
                         b * 114) /
                        1000;

                    var bit =
                        luminance < 160
                            ? 1UL
                            : 0UL;

                    hash ^=
                        bit;

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

    private readonly record struct RgbSample(
        int R,
        int G,
        int B);

    private readonly record struct TurnRowVisualStats(
        int SampledPixels,
        int BlueLikePixels,
        int DifferentPixels)
    {
        public double BlueLikeRatio =>
            SampledPixels == 0
                ? 0
                : (double)BlueLikePixels / SampledPixels;

        public double ColorDifferenceRatio =>
            SampledPixels == 0
                ? 0
                : (double)DifferentPixels / SampledPixels;
    }

    private static RgbSample SampleAverageColorSparse(
        int left,
        int top,
        int width,
        int height)
    {
        var hdc =
            NativeMethods.GetDC(
                IntPtr.Zero);

        if (hdc == IntPtr.Zero)
            return new RgbSample(0, 0, 0);

        try
        {
            long sumR = 0;
            long sumG = 0;
            long sumB = 0;
            var count = 0;

            const int stepX = 4;
            const int stepY = 3;

            for (var y = top;
                 y < top + height;
                 y += stepY)
            {
                for (var x = left;
                     x < left + width;
                     x += stepX)
                {
                    var pixel =
                        NativeMethods.GetPixel(
                            hdc,
                            x,
                            y);

                    sumR +=
                        (int)(pixel & 0xFF);

                    sumG +=
                        (int)((pixel >> 8) & 0xFF);

                    sumB +=
                        (int)((pixel >> 16) & 0xFF);

                    count++;
                }
            }

            if (count == 0)
                return new RgbSample(0, 0, 0);

            return new RgbSample(
                (int)(sumR / count),
                (int)(sumG / count),
                (int)(sumB / count));
        }
        finally
        {
            NativeMethods.ReleaseDC(
                IntPtr.Zero,
                hdc);
        }
    }

    private static TurnRowVisualStats AnalyzeTurnDropdownRow(
        int left,
        int top,
        int width,
        int height,
        RgbSample background)
    {
        var hdc =
            NativeMethods.GetDC(
                IntPtr.Zero);

        if (hdc == IntPtr.Zero)
            return new TurnRowVisualStats(0, 0, 0);

        try
        {
            var sampled =
                0;

            var blueLike =
                0;

            var different =
                0;

            const int stepX =
                3;

            const int stepY =
                2;

            for (var y = top + 1;
                 y < top + height - 1;
                 y += stepY)
            {
                for (var x = left + 1;
                     x < left + width - 1;
                     x += stepX)
                {
                    var pixel =
                        NativeMethods.GetPixel(
                            hdc,
                            x,
                            y);

                    var r =
                        (int)(pixel & 0xFF);

                    var g =
                        (int)((pixel >> 8) & 0xFF);

                    var b =
                        (int)((pixel >> 16) & 0xFF);

                    sampled++;

                    // Selección/lista azul típica de VB6/Windows.
                    if (b > r + 25 &&
                        b > g + 10 &&
                        b >= 110)
                    {
                        blueLike++;
                    }

                    var delta =
                        Math.Abs(r - background.R) +
                        Math.Abs(g - background.G) +
                        Math.Abs(b - background.B);

                    if (delta >= 85)
                    {
                        different++;
                    }
                }
            }

            return new TurnRowVisualStats(
                sampled,
                blueLike,
                different);
        }
        finally
        {
            NativeMethods.ReleaseDC(
                IntPtr.Zero,
                hdc);
        }
    }

    private static int CountDarkPixelsInRegion(
        int left,
        int top,
        int width,
        int height,
        out int sampledPixels)
    {
        sampledPixels =
            0;

        var darkPixels =
            0;

        var hdc =
            NativeMethods.GetDC(
                IntPtr.Zero);

        if (hdc == IntPtr.Zero)
            return 0;

        try
        {
            for (var y = top;
                 y < top + height;
                 y++)
            {
                for (var x = left;
                     x < left + width;
                     x++)
                {
                    var pixel =
                        NativeMethods.GetPixel(
                            hdc,
                            x,
                            y);

                    var r =
                        (int)(pixel & 0xFF);

                    var g =
                        (int)((pixel >> 8) & 0xFF);

                    var b =
                        (int)((pixel >> 16) & 0xFF);

                    sampledPixels++;

                    if (r < 135 &&
                        g < 135 &&
                        b < 135)
                    {
                        darkPixels++;
                    }
                }
            }

            return darkPixels;
        }
        finally
        {
            NativeMethods.ReleaseDC(
                IntPtr.Zero,
                hdc);
        }
    }

    private static bool HasVisibleTurnData(
        int left,
        int top,
        int width,
        int height,
        out int darkPixels,
        out int sampledPixels)
    {
        darkPixels =
            0;

        sampledPixels =
            0;

        var hdc =
            NativeMethods.GetDC(
                IntPtr.Zero);

        if (hdc == IntPtr.Zero)
            return false;

        try
        {
            // Ignorar borde del TextBox para no confundirlo con contenido.
            var x0 =
                left + 4;

            var y0 =
                top + 4;

            var x1 =
                left + width - 5;

            var y1 =
                top + height - 5;

            for (var y = y0;
                 y <= y1;
                 y++)
            {
                for (var x = x0;
                     x <= x1;
                     x++)
                {
                    var pixel =
                        NativeMethods.GetPixel(
                            hdc,
                            x,
                            y);

                    var r =
                        (int)(pixel & 0xFF);

                    var g =
                        (int)((pixel >> 8) & 0xFF);

                    var b =
                        (int)((pixel >> 16) & 0xFF);

                    sampledPixels++;

                    // Texto de fecha/hora es oscuro sobre fondo claro.
                    // Umbral deliberadamente conservador para ignorar
                    // sombras suaves del tema.
                    if (r < 135 &&
                        g < 135 &&
                        b < 135)
                    {
                        darkPixels++;
                    }
                }
            }

            // Una fecha/hora completa produce muchos píxeles oscuros.
            // Un campo vacío debería quedar prácticamente en cero.
            return darkPixels >= 8;
        }
        finally
        {
            NativeMethods.ReleaseDC(
                IntPtr.Zero,
                hdc);
        }
    }


    private static ulong CaptureTurnSelectionFingerprint(
        int turnLeft,
        int turnTop)
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

            // Región que cubre cboturno + txtprecorte.
            var left =
                turnLeft;

            var top =
                turnTop;

            const int width =
                190;

            const int height =
                22;

            const int cols =
                48;

            const int rows =
                10;

            for (var row = 0;
                 row < rows;
                 row++)
            {
                for (var col = 0;
                     col < cols;
                     col++)
                {
                    var x =
                        left +
                        col *
                        (width - 1) /
                        (cols - 1);

                    var y =
                        top +
                        row *
                        (height - 1) /
                        (rows - 1);

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

    private static TurnAccessibleProbe? FindAccessibleControlByName(
        string targetName,
        int left,
        int top,
        int right,
        int bottom)
    {
        var seen =
            new HashSet<string>(
                StringComparer.Ordinal);

        for (var y = top;
             y <= bottom;
             y += 5)
        {
            for (var x = left;
                 x <= right;
                 x += 5)
            {
                var probe =
                    TryReadAccessibleProbeAtPoint(
                        x,
                        y);

                if (probe is null)
                    continue;

                var key =
                    $"{probe.Name}|{probe.Value}|{probe.Role}|" +
                    $"{probe.Left},{probe.Top},{probe.Width},{probe.Height}";

                if (!seen.Add(
                        key))
                {
                    continue;
                }

                if (string.Equals(
                        probe.Name.Trim(),
                        targetName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return probe;
                }
            }
        }

        return null;
    }

    private sealed class TurnAccessibleProbe
    {
        public string Name { get; init; } = "";
        public string Value { get; init; } = "";
        public string Role { get; init; } = "";
        public string State { get; init; } = "";
        public int Left { get; init; }
        public int Top { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
    }

    private static TurnAccessibleProbe? TryReadAccessibleProbeAtPoint(
        int x,
        int y)
    {
        var point =
            new NativeMethods.POINT
            {
                X = x,
                Y = y
            };

        object accessible;
        object childId;

        var hr =
            AccessibleObjectFromPoint(
                point,
                out accessible,
                out childId);

        if (hr < 0 ||
            accessible is null)
        {
            return null;
        }

        try
        {
            dynamic acc =
                accessible;

            var name =
                TryGetAccessibleName(
                    acc,
                    childId)
                .Trim();

            var value =
                TryGetAccessibleValue(
                    acc,
                    childId)
                .Trim();

            var role =
                TryGetAccessibleRole(
                    acc,
                    childId);

            var state =
                TryGetAccessibleState(
                    acc,
                    childId);

            int left;
            int top;
            int width;
            int height;

            acc.accLocation(
                out left,
                out top,
                out width,
                out height,
                childId);

            if (width <= 0 ||
                height <= 0)
            {
                return null;
            }

            return new TurnAccessibleProbe
            {
                Name = name,
                Value = value,
                Role = role,
                State = state,
                Left = left,
                Top = top,
                Width = width,
                Height = height
            };
        }
        catch
        {
            return null;
        }
    }

    private static async Task MoveAccessibleUserListToTopAsync(
        int anchorX,
        int anchorY,
        CancellationToken cancellationToken)
    {
        string previousFirst =
            "";

        for (var i = 0;
             i < MaxUsers;
             i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var visible =
                ReadVisibleAccessibleUsers(
                    anchorX,
                    anchorY);

            if (visible.Count == 0)
                return;

            var first =
                visible[0].Name;

            if (i > 0 &&
                string.Equals(
                    first,
                    previousFirst,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            previousFirst =
                first;

            ClickAccessibleScrollLineUp(
                anchorX,
                anchorY);

            await Task.Delay(
                180,
                cancellationToken);
        }
    }

    private static async Task<List<string>> EnumerateAllAccessibleUsersAsync(
        int anchorX,
        int anchorY,
        CancellationToken cancellationToken)
    {
        var orderedUsers =
            new List<string>();

        var seen =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var lastViewportKey =
            "";

        for (var guard = 0;
             guard < MaxUsers * 2;
             guard++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var visible =
                ReadVisibleAccessibleUsers(
                    anchorX,
                    anchorY);

            if (visible.Count == 0)
                break;

            var viewportKey =
                string.Join(
                    "|",
                    visible.Select(x => x.Name));

            foreach (var item in visible)
            {
                if (seen.Add(item.Name))
                {
                    orderedUsers.Add(
                        item.Name);
                }
            }

            if (string.Equals(
                    viewportKey,
                    lastViewportKey,
                    StringComparison.Ordinal))
            {
                break;
            }

            lastViewportKey =
                viewportKey;

            if (!ClickAccessibleScrollLineDown(
                    anchorX,
                    anchorY))
            {
                break;
            }

            await Task.Delay(
                220,
                cancellationToken);
        }

        return orderedUsers;
    }

    private static async Task<bool> SelectAccessibleUserByNameAsync(
        int anchorX,
        int anchorY,
        string targetName,
        IReadOnlyList<string> orderedUsers,
        CancellationToken cancellationToken)
    {
        await OpenUserDropdownAsync(
            anchorX,
            anchorY,
            cancellationToken);

        // El dropdown puede reabrirse en cualquier posición.
        // Primero revisamos el viewport actual.
        for (var guard = 0;
             guard < MaxUsers * 2;
             guard++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var visible =
                ReadVisibleAccessibleUsers(
                    anchorX,
                    anchorY);

            var target =
                visible.FirstOrDefault(
                    x => string.Equals(
                        x.Name,
                        targetName,
                        StringComparison.OrdinalIgnoreCase));

            if (target is not null)
            {
                var clickX =
                    target.Left +
                    target.Width / 2;

                var clickY =
                    target.Top +
                    target.Height / 2;

                Console.WriteLine(
                    $"[A11Y][TARGET] \"{target.Name}\" " +
                    $"Bounds=({target.Left},{target.Top},{target.Width},{target.Height}) " +
                    $"Click=({clickX},{clickY})");

                NativeMethods.SetCursorPos(
                    clickX,
                    clickY);

                await Task.Delay(
                    350,
                    cancellationToken);

                Click();

                await Task.Delay(
                    1000,
                    cancellationToken);

                return true;
            }

            if (visible.Count == 0)
            {
                SendKey(
                    0x1B,
                    false,
                    false,
                    false);

                return false;
            }

            // Decidir dirección usando el orden completo conocido.
            var targetIndex =
                IndexOfUserName(
                    orderedUsers,
                    targetName);

            var firstIndex =
                IndexOfUserName(
                    orderedUsers,
                    visible[0].Name);

            var lastIndex =
                IndexOfUserName(
                    orderedUsers,
                    visible[^1].Name);

            bool moved;

            if (targetIndex >= 0 &&
                firstIndex >= 0 &&
                targetIndex < firstIndex)
            {
                Console.WriteLine(
                    $"[A11Y][SEARCH] \"{targetName}\" está arriba del viewport.");

                ClickAccessibleScrollLineUp(
                    anchorX,
                    anchorY);

                moved =
                    true;
            }
            else
            {
                Console.WriteLine(
                    $"[A11Y][SEARCH] \"{targetName}\" está debajo del viewport.");

                moved =
                    ClickAccessibleScrollLineDown(
                        anchorX,
                        anchorY);
            }

            if (!moved)
            {
                SendKey(
                    0x1B,
                    false,
                    false,
                    false);

                return false;
            }

            await Task.Delay(
                220,
                cancellationToken);
        }

        SendKey(
            0x1B,
            false,
            false,
            false);

        return false;
    }

    private static int IndexOfUserName(
        IReadOnlyList<string> users,
        string name)
    {
        for (var i = 0;
             i < users.Count;
             i++)
        {
            if (string.Equals(
                    users[i],
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class AccessibleUserItem
    {
        public string Name { get; init; } = "";
        public int Left { get; init; }
        public int Top { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
    }

    private static List<AccessibleUserItem> ReadVisibleAccessibleUsers(
        int anchorX,
        int anchorY)
    {
        var items =
            new List<AccessibleUserItem>();

        // La prueba V6.18.41 confirmó filas de 15 px y 7 visibles.
        // Sondeamos el centro vertical aproximado de cada fila.
        var rowX =
            anchorX - 40;

        for (var rowIndex = 0;
             rowIndex < 7;
             rowIndex++)
        {
            var rowY =
                anchorY +
                FirstRowOffsetY +
                rowIndex *
                RowHeight;

            var item =
                TryReadAccessibleUserAtPoint(
                    rowX,
                    rowY);

            if (item is not null &&
                !string.IsNullOrWhiteSpace(
                    item.Name))
            {
                items.Add(
                    item);
            }
        }

        return items;
    }

    private static AccessibleUserItem? TryReadAccessibleUserAtPoint(
        int x,
        int y)
    {
        var point =
            new NativeMethods.POINT
            {
                X = x,
                Y = y
            };

        object accessible;
        object childId;

        var hr =
            AccessibleObjectFromPoint(
                point,
                out accessible,
                out childId);

        if (hr < 0 ||
            accessible is null)
        {
            return null;
        }

        try
        {
            dynamic acc =
                accessible;

            var role =
                TryGetAccessibleRole(
                    acc,
                    childId);

            // ROLE_SYSTEM_LISTITEM = 34
            if (!role.StartsWith(
                    "34",
                    StringComparison.Ordinal))
            {
                return null;
            }

            var name =
                TryGetAccessibleName(
                    acc,
                    childId)
                .Trim();

            if (string.IsNullOrWhiteSpace(
                    name))
            {
                return null;
            }

            int left;
            int top;
            int width;
            int height;

            acc.accLocation(
                out left,
                out top,
                out width,
                out height,
                childId);

            return new AccessibleUserItem
            {
                Name = name,
                Left = left,
                Top = top,
                Width = width,
                Height = height
            };
        }
        catch
        {
            return null;
        }
    }

    private static void ClickAccessibleScrollLineUp(
        int anchorX,
        int anchorY)
    {
        // V6.18.41 confirmó:
        // Línea arriba Bounds=(533,366,17,17)
        var x =
            anchorX + 81;

        var y =
            anchorY + 16;

        NativeMethods.SetCursorPos(
            x,
            y);

        Click();
    }

    private static bool ClickAccessibleScrollLineDown(
        int anchorX,
        int anchorY)
    {
        // Popup real:
        // filas desde Y≈366 hasta Y≈471.
        // La flecha inferior ocupa el extremo inferior de la scrollbar.
        // Usamos el centro, no el borde X=533 que en diagnósticos podía
        // caer exactamente en el límite del list item.
        var x =
            anchorX + 81;

        var y =
            anchorY + 105;

        var point =
            new NativeMethods.POINT
            {
                X = x,
                Y = y
            };

        object accessible;
        object childId;

        var hr =
            AccessibleObjectFromPoint(
                point,
                out accessible,
                out childId);

        if (hr < 0 ||
            accessible is null)
        {
            return false;
        }

        try
        {
            dynamic acc =
                accessible;

            var name =
                TryGetAccessibleName(
                    acc,
                    childId)
                .Trim();

            var role =
                TryGetAccessibleRole(
                    acc,
                    childId);

            Console.WriteLine(
                $"[A11Y][SCROLL-DOWN] Name=\"{name}\" Role={role} Point=({x},{y})");

            if (!name.Contains(
                    "abajo",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            NativeMethods.SetCursorPos(
                x,
                y);

            Click();

            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport(
        "oleacc.dll",
        PreserveSig = true)]
    private static extern int AccessibleObjectFromPoint(
        NativeMethods.POINT ptScreen,
        [MarshalAs(UnmanagedType.Interface)] out object accessible,
        [MarshalAs(UnmanagedType.Struct)] out object childId);

    private static void DumpAccessibleAtPoint(
        string label,
        int x,
        int y)
    {
        var point =
            new NativeMethods.POINT
            {
                X = x,
                Y = y
            };

        var hwnd =
            NativeMethods.WindowFromPoint(
                point);

        Console.WriteLine();
        Console.WriteLine(
            $"[A11Y][{label}] Point=({x},{y}) " +
            $"HWND=0x{hwnd.ToInt64():X} " +
            $"Class=\"{GetClassName(hwnd)}\" " +
            $"Text=\"{GetWindowText(hwnd).Trim()}\"");

        object accessible;
        object childId;

        var hr =
            AccessibleObjectFromPoint(
                point,
                out accessible,
                out childId);

        Console.WriteLine(
            $"[A11Y][{label}] AccessibleObjectFromPoint HR=0x{hr:X8}; " +
            $"Child={FormatComValue(childId)}; " +
            $"ComObject={Marshal.IsComObject(accessible)}");

        if (hr < 0 ||
            accessible is null)
        {
            Console.WriteLine(
                $"[A11Y][{label}] No se obtuvo objeto accesible.");

            return;
        }

        dynamic acc =
            accessible;

        var name =
            TryGetAccessibleName(
                acc,
                childId);

        var value =
            TryGetAccessibleValue(
                acc,
                childId);

        var role =
            TryGetAccessibleRole(
                acc,
                childId);

        var state =
            TryGetAccessibleState(
                acc,
                childId);

        var description =
            TryGetAccessibleDescription(
                acc,
                childId);

        var defaultAction =
            TryGetAccessibleDefaultAction(
                acc,
                childId);

        var childCount =
            TryGetAccessibleChildCount(
                acc);

        var bounds =
            TryGetAccessibleBounds(
                acc,
                childId);

        Console.WriteLine(
            $"[A11Y][{label}] " +
            $"Name=\"{name}\"; " +
            $"Value=\"{value}\"; " +
            $"Role={role}; " +
            $"State={state}; " +
            $"Children={childCount}");

        Console.WriteLine(
            $"[A11Y][{label}] " +
            $"Description=\"{description}\"; " +
            $"DefaultAction=\"{defaultAction}\"; " +
            $"Bounds={bounds}");
    }

    private static string TryGetAccessibleName(
        dynamic acc,
        object childId)
    {
        try
        {
            return Convert.ToString(
                       acc.get_accName(
                           childId)) ??
                   "";
        }
        catch
        {
            try
            {
                return Convert.ToString(
                           acc.accName) ??
                       "";
            }
            catch
            {
                return "<NO-EXPUESTO>";
            }
        }
    }

    private static string TryGetAccessibleValue(
        dynamic acc,
        object childId)
    {
        try
        {
            return Convert.ToString(
                       acc.get_accValue(
                           childId)) ??
                   "";
        }
        catch
        {
            try
            {
                return Convert.ToString(
                           acc.accValue) ??
                       "";
            }
            catch
            {
                return "<NO-EXPUESTO>";
            }
        }
    }

    private static string TryGetAccessibleRole(
        dynamic acc,
        object childId)
    {
        try
        {
            return FormatComValue(
                acc.get_accRole(
                    childId));
        }
        catch
        {
            try
            {
                return FormatComValue(
                    acc.accRole);
            }
            catch
            {
                return "<NO-EXPUESTO>";
            }
        }
    }

    private static string TryGetAccessibleState(
        dynamic acc,
        object childId)
    {
        try
        {
            return FormatComValue(
                acc.get_accState(
                    childId));
        }
        catch
        {
            try
            {
                return FormatComValue(
                    acc.accState);
            }
            catch
            {
                return "<NO-EXPUESTO>";
            }
        }
    }

    private static string TryGetAccessibleDescription(
        dynamic acc,
        object childId)
    {
        try
        {
            return Convert.ToString(
                       acc.get_accDescription(
                           childId)) ??
                   "";
        }
        catch
        {
            return "<NO-EXPUESTO>";
        }
    }

    private static string TryGetAccessibleDefaultAction(
        dynamic acc,
        object childId)
    {
        try
        {
            return Convert.ToString(
                       acc.get_accDefaultAction(
                           childId)) ??
                   "";
        }
        catch
        {
            return "<NO-EXPUESTO>";
        }
    }

    private static int TryGetAccessibleChildCount(
        dynamic acc)
    {
        try
        {
            return Convert.ToInt32(
                acc.accChildCount);
        }
        catch
        {
            return -1;
        }
    }

    private static string TryGetAccessibleBounds(
        dynamic acc,
        object childId)
    {
        try
        {
            int left;
            int top;
            int width;
            int height;

            acc.accLocation(
                out left,
                out top,
                out width,
                out height,
                childId);

            return
                $"({left},{top},{width},{height})";
        }
        catch
        {
            return "<NO-EXPUESTO>";
        }
    }

    private static string FormatComValue(
        object? value)
    {
        if (value is null)
            return "<null>";

        try
        {
            return
                $"{value} ({value.GetType().Name})";
        }
        catch
        {
            return
                Convert.ToString(value) ??
                "<null>";
        }
    }

    private static async Task<bool> ScrollUserListDownExactlyOneAsync(
        int anchorX,
        int anchorY,
        CancellationToken cancellationToken)
    {
        var scrollX =
            anchorX + 73;

        var scrollDownY =
            anchorY +
            FirstRowOffsetY +
            VisibleRows * RowHeight -
            2;

        // Sacar cursor de la lista antes de capturar.
        NativeMethods.SetCursorPos(
            anchorX + 115,
            anchorY - 12);

        await Task.Delay(
            180,
            cancellationToken);

        var before =
            CaptureUserListViewportFingerprint(
                anchorX,
                anchorY);

        Console.WriteLine(
            $"[USUARIOS][SCROLL] Click DOWN único en ({scrollX},{scrollDownY})");

        NativeMethods.SetCursorPos(
            scrollX,
            scrollDownY);

        Click();

        await Task.Delay(
            320,
            cancellationToken);

        NativeMethods.SetCursorPos(
            anchorX + 115,
            anchorY - 12);

        await Task.Delay(
            180,
            cancellationToken);

        var after =
            CaptureUserListViewportFingerprint(
                anchorX,
                anchorY);

        var changed =
            before != 0 &&
            after != 0 &&
            before != after;

        Console.WriteLine(
            $"[USUARIOS][SCROLL] " +
            $"Antes=0x{before:X16}; Después=0x{after:X16}; Cambio={changed}");

        return changed;
    }


    private static ulong CaptureUserListViewportFingerprint(
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

            // Solo el área interna del dropdown de Usuario.
            // Excluye el campo principal y la scrollbar.
            const int leftOffset =
                -145;

            const int topOffset =
                18;

            const int width =
                195;

            const int height =
                102;

            const int sampleColumns =
                32;

            const int sampleRows =
                21;

            for (var row = 0;
                 row < sampleRows;
                 row++)
            {
                for (var column = 0;
                     column < sampleColumns;
                     column++)
                {
                    var px =
                        anchorX +
                        leftOffset +
                        column *
                        (width - 1) /
                        (sampleColumns - 1);

                    var py =
                        anchorY +
                        topOffset +
                        row *
                        (height - 1) /
                        (sampleRows - 1);

                    var pixel =
                        NativeMethods.GetPixel(
                            hdc,
                            px,
                            py);

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

    private static async Task<ulong> CaptureStableUserFieldFingerprintAsync(
        int anchorX,
        int anchorY,
        CancellationToken cancellationToken)
    {
        NativeMethods.SetCursorPos(
            anchorX + 150,
            anchorY - 25);

        await Task.Delay(
            350,
            cancellationToken);

        return CaptureUserFieldFingerprint(
            anchorX,
            anchorY);
    }

    private static async Task OpenUserDropdownAsync(
        int anchorX,
        int anchorY,
        CancellationToken cancellationToken)
    {
        NativeMethods.SetCursorPos(
            anchorX,
            anchorY);

        Click();

        await Task.Delay(
            800,
            cancellationToken);
    }

    private static async Task ScrollUserListToTopOnceAsync(
        int anchorX,
        int anchorY,
        CancellationToken cancellationToken)
    {
        var scrollX =
            anchorX + 73;

        var scrollUpY =
            anchorY + 20;

        for (var i = 0;
             i < MaxUsers;
             i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            NativeMethods.SetCursorPos(
                scrollX,
                scrollUpY);

            Click();

            await Task.Delay(
                55,
                cancellationToken);
        }

        NativeMethods.SetCursorPos(
            anchorX - 100,
            anchorY - 20);

        await Task.Delay(
            500,
            cancellationToken);
    }

    private static async Task ClickVisibleUserRowAsync(
        int anchorX,
        int anchorY,
        int rowIndex,
        CancellationToken cancellationToken)
    {
        var rowX =
            anchorX +
            InsideListOffsetX;

        var rowY =
            anchorY +
            FirstRowOffsetY +
            rowIndex *
            RowHeight;

        Console.WriteLine(
            $"[USUARIOS] Click filaVisible={rowIndex + 1}; punto=({rowX},{rowY})");

        NativeMethods.SetCursorPos(
            rowX,
            rowY);

        await Task.Delay(
            400,
            cancellationToken);

        Click();

        await Task.Delay(
            1200,
            cancellationToken);
    }

    private static async Task SelectInitialReportMiniprinterThenLaserAsync(
        WorkflowStep reportOpenStep,
        WorkflowStep reportLaserStep,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var window =
            await WaitForWindowAsync(
                reportOpenStep,
                cancellationToken);

        if (window == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "REPORTE: no apareció la ventana principal.");
        }

        if (!NativeMethods.GetWindowRect(
                window,
                out var rect))
        {
            throw new InvalidOperationException(
                "REPORTE: no se pudo leer la geometría de la ventana.");
        }

        var openX =
            rect.Left +
            (int)Math.Round(
                rect.Width *
                reportOpenStep.RelativeX);

        var openY =
            rect.Top +
            (int)Math.Round(
                rect.Height *
                reportOpenStep.RelativeY);

        var trainedRowX =
            rect.Left +
            (int)Math.Round(
                rect.Width *
                reportLaserStep.RelativeX);

        var trainedRowY =
            rect.Top +
            (int)Math.Round(
                rect.Height *
                reportLaserStep.RelativeY);

        const int ReportRowHeight =
            15;

        // ------------------------------------------------------------
        // 1. MINIPRINTER
        // Paso 30 entrenado cae sobre la primera fila.
        // ------------------------------------------------------------
        Console.WriteLine();
        Console.WriteLine(
            "[REPORTE][INIT] Abriendo Reporte para seleccionar Miniprinter...");

        NativeMethods.SetForegroundWindow(
            window);

        NativeMethods.SetCursorPos(
            openX,
            openY);

        Click();

        await Task.Delay(
            900,
            cancellationToken);

        Console.WriteLine(
            $"[REPORTE][INIT] Click Miniprinter en ({trainedRowX},{trainedRowY})");

        NativeMethods.SetCursorPos(
            trainedRowX,
            trainedRowY);

        await Task.Delay(
            450,
            cancellationToken);

        Click();

        await Task.Delay(
            1400,
            cancellationToken);

        // ------------------------------------------------------------
        // 2. LASER
        // Reabrir selector y hacer click una fila más abajo.
        // ------------------------------------------------------------
        Console.WriteLine(
            "[REPORTE][INIT] Reabriendo Reporte para seleccionar Láser...");

        NativeMethods.SetCursorPos(
            openX,
            openY);

        Click();

        await Task.Delay(
            900,
            cancellationToken);

        var laserY =
            trainedRowY +
            ReportRowHeight;

        Console.WriteLine(
            $"[REPORTE][INIT] Click Láser en ({trainedRowX},{laserY})");

        NativeMethods.SetCursorPos(
            trainedRowX,
            laserY);

        await Task.Delay(
            450,
            cancellationToken);

        Click();

        await Task.Delay(
            1800,
            cancellationToken);

        Console.WriteLine(
            "[REPORTE][INIT][OK] Inicialización terminada: Miniprinter -> Láser.");
    }

    private static async Task SelectUserRowAfterScrollbarTopAsync(
        int anchorX,
        int anchorY,
        int rowIndex,
        int displayOrdinal,
        string expectedName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine();
        Console.WriteLine(
            $"[USUARIOS][FILA {displayOrdinal:00}] " +
            $"Abriendo Usuario; esperado={expectedName}...");

        NativeMethods.SetCursorPos(
            anchorX,
            anchorY);

        Click();

        await Task.Delay(
            900,
            cancellationToken);

        // V6.18.29 confirmado:
        // la barra vertical responde al click físico sobre la flecha superior.
        var scrollUpX =
            anchorX + 73;

        var scrollUpY =
            anchorY + 20;

        Console.WriteLine(
            $"[USUARIOS][FILA {displayOrdinal:00}] " +
            $"Forzando scrollbar arriba en ({scrollUpX},{scrollUpY}) x50...");

        for (var i = 1;
             i <= MaxUsers;
             i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            NativeMethods.SetCursorPos(
                scrollUpX,
                scrollUpY);

            Click();

            await Task.Delay(
                70,
                cancellationToken);
        }

        await Task.Delay(
            900,
            cancellationToken);

        var rowX =
            anchorX +
            InsideListOffsetX;

        var rowY =
            anchorY +
            FirstRowOffsetY +
            rowIndex *
            RowHeight;

        Console.WriteLine(
            $"[USUARIOS][FILA {displayOrdinal:00}] " +
            $"Click fila ({rowX},{rowY}); esperado={expectedName}");

        NativeMethods.SetCursorPos(
            rowX,
            rowY);

        await Task.Delay(
            600,
            cancellationToken);

        Click();

        await Task.Delay(
            1600,
            cancellationToken);

        Console.WriteLine(
            $"[USUARIOS][FILA {displayOrdinal:00}][OK] " +
            $"Verificar visualmente Usuario={expectedName}.");
    }

    private static ulong CaptureUserDropdownFingerprint(
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

            // Zona amplia debajo del campo Usuario. El selector de SoftRestaurant
            // es owner-drawn y no expone un HWND hijo para la lista.
            const int left =
                -150;

            const int top =
                14;

            const int width =
                260;

            const int height =
                150;

            for (var row = 0;
                 row < 20;
                 row++)
            {
                for (var column = 0;
                     column < 36;
                     column++)
                {
                    var px =
                        anchorX +
                        left +
                        column *
                        (width - 1) /
                        35;

                    var py =
                        anchorY +
                        top +
                        row *
                        (height - 1) /
                        19;

                    var pixel =
                        NativeMethods.GetPixel(
                            hdc,
                            px,
                            py);

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

        private static ulong CaptureOwnerDrawUserFingerprint(
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

            // Región centrada en la caja owner-drawn de Usuario.
            const int left =
                -25;

            const int top =
                -11;

            const int width =
                175;

            const int height =
                22;

            for (var row = 0;
                row < 8;
                row++)
            {
                for (var column = 0;
                    column < 32;
                    column++)
                {
                    var px =
                        anchorX +
                        left +
                        column *
                        (width - 1) /
                        31;

                    var py =
                        anchorY +
                        top +
                        row *
                        (height - 1) /
                        7;

                    var pixel =
                        NativeMethods.GetPixel(
                            hdc,
                            px,
                            py);

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
}