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
            "=== CIERRES V6.18.26 - OPEN + FECHA OK + USUARIO POR FILA DIRECTA ===");

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
        await RunUsersSequentialDiagnosticAsync(
            userAnchor,
            cancellationToken);

        Console.WriteLine();
        Console.WriteLine(
            "[V6.18] Diagnóstico terminado. NO se ejecutaron cierres.");
        Console.WriteLine(
            "[V6.18] Revisa el diagnóstico [USUARIOS][02] en consola.");

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

        // V6.18.14:
        // El MonthView se abre con la fecha actual del DTPicker seleccionada.
        // En este flujo ese valor es HOY. Para seleccionar AYER de forma
        // determinista no usamos coordenadas: LEFT mueve un día atrás y
        // ENTER confirma/cierra el calendario.
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

    private static async Task RunUsersSequentialDiagnosticAsync(
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
            "=== SELECCION DIRECTA DE FILAS USUARIO V6.18.26 ===");

        Console.WriteLine(
            $"[USUARIOS] Anchor=({x},{y})");

        Console.WriteLine(
            $"[USUARIOS] Geometría lista: X={x + InsideListOffsetX}; " +
            $"FirstRowOffsetY={FirstRowOffsetY}; RowHeight={RowHeight}");

        // ------------------------------------------------------------
        // PRUEBA 1: seleccionar FILA 2 directamente.
        // En el video actual corresponde visualmente a BOSPINA.
        // ------------------------------------------------------------
        await SelectUserRowDirectAsync(
            x,
            y,
            rowIndex: 1,
            displayOrdinal: 2,
            cancellationToken);

        Console.WriteLine(
            "[USUARIOS] Pausa para verificar visualmente la FILA 2...");

        await Task.Delay(
            2500,
            cancellationToken);

        // ------------------------------------------------------------
        // PRUEBA 2: seleccionar FILA 3 directamente.
        // En el video actual corresponde visualmente a SANCHEZ.
        // ------------------------------------------------------------
        await SelectUserRowDirectAsync(
            x,
            y,
            rowIndex: 2,
            displayOrdinal: 3,
            cancellationToken);

        Console.WriteLine(
            "[USUARIOS] Pausa para verificar visualmente la FILA 3...");

        await Task.Delay(
            2500,
            cancellationToken);

        Console.WriteLine();
        Console.WriteLine(
            "[USUARIOS] V6.18.26 terminado.");

        Console.WriteLine(
            "[USUARIOS] NO se usaron hashes para decidir éxito.");

        Console.WriteLine(
            "[USUARIOS] NO se tocó Reporte, Turno, Destino ni Exportación.");
    }

    private static async Task SelectUserRowDirectAsync(
        int anchorX,
        int anchorY,
        int rowIndex,
        int displayOrdinal,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine();
        Console.WriteLine(
            $"[USUARIOS][FILA {displayOrdinal:00}] Abriendo Usuario...");

        NativeMethods.SetCursorPos(
            anchorX,
            anchorY);

        Click();

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
            $"Click directo en ({rowX},{rowY})");

        NativeMethods.SetCursorPos(
            rowX,
            rowY);

        await Task.Delay(
            500,
            cancellationToken);

        Click();

        await Task.Delay(
            1600,
            cancellationToken);

        Console.WriteLine(
            $"[USUARIOS][FILA {displayOrdinal:00}] Click completado. " +
            "Verificar nombre visible en Usuario.");
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