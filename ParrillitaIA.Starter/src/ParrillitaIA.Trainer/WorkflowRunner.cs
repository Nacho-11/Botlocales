using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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
            "=== CIERRES V3.8.7 - RECORRIDO ESTRICTO SIN SALTOS ===");

        var excelPidsBeforeRun =
            Process.GetProcessesByName("EXCEL")
                .Select(p => p.Id)
                .ToHashSet();

        try
        {
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

            // Si la raíz configurada ya termina en el año, no duplicarlo.
            var rootLeaf =
                Path.GetFileName(
                    outputRoot.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar));

            var outputDirectory =
                string.Equals(
                    rootLeaf,
                    reportDate.ToString("yyyy"),
                    StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(
                        outputRoot,
                        reportDate.ToString("MM"))
                    : Path.Combine(
                        outputRoot,
                        reportDate.ToString("yyyy"),
                        reportDate.ToString("MM"));

            Directory.CreateDirectory(outputDirectory);

            Console.WriteLine(
                $"[GUARDADO] Carpeta destino: {outputDirectory}");

            var usersChecked = 0;
            var closuresFound = 0;
            var filesSaved = 0;

            var reviewedHashes =
                new HashSet<ulong>();

            var reviewedNames =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            var selectorProbe =
                await FocusUserSelectorAsync(
                    userAnchor,
                    cancellationToken);

            var userCombo =
                FindUserComboHandle(
                    selectorProbe);

            var comboCount =
                GetComboItemCount(
                    userCombo);

            if (comboCount > 0)
            {
                Console.WriteLine(
                    $"[USUARIOS] ComboBox real detectado HWND=0x{userCombo.ToInt64():X}; " +
                    $"usuarios en lista={comboCount}.");
            }
            else
            {
                Console.WriteLine(
                    "[USUARIOS] El control no expuso CB_GETCOUNT. " +
                    "Se usará recorrido visual con control de usuarios ya revisados.");
            }

            var maxIterations =
                comboCount > 0
                    ? comboCount
                    : MaxUsers;

            if (comboCount > 0)
            {
                Console.WriteLine("[USUARIOS] Orden exacto detectado en el ComboBox:");

                for (var i = 0; i < comboCount; i++)
                {
                    var itemText =
                        GetComboItemText(
                            userCombo,
                            i);

                    Console.WriteLine(
                        $"  #{i + 1}: {(string.IsNullOrWhiteSpace(itemText) ? "(sin texto)" : itemText)}");
                }
            }

            var consecutiveDuplicates = 0;

            for (var ordinal = 0;
                 ordinal < maxIterations;
                 ordinal++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                SelectionResult current;
                string currentUser = string.Empty;

                if (comboCount > 0 &&
                    userCombo != IntPtr.Zero)
                {
                    current =
                        await SelectComboUserByIndexAsync(
                            userAnchor,
                            userCombo,
                            ordinal,
                            cancellationToken);

                    // El nombre se toma del MISMO índice solicitado, no del texto
                    // visual potencialmente retrasado del ComboBox. Así índice 0 siempre
                    // corresponde al primer usuario y ningún índice puede "correr" el nombre.
                    currentUser =
                        GetComboItemText(
                            userCombo,
                            ordinal);

                    if (string.IsNullOrWhiteSpace(currentUser))
                    {
                        currentUser =
                            TryReadComboSelectedText(
                                userCombo);
                    }

                    if (string.IsNullOrWhiteSpace(currentUser))
                    {
                        currentUser =
                            await ReadSelectedUserNameByClipboardAsync(
                                current,
                                cancellationToken);
                    }

                    if (string.IsNullOrWhiteSpace(currentUser))
                    {
                        currentUser =
                            await ReadSelectedUserNameAsync(
                                current,
                                cancellationToken);
                    }
                }
                else
                {
                    current =
                        await SelectUserByAbsoluteIndexAsync(
                            userAnchor,
                            ordinal,
                            cancellationToken);

                    currentUser =
                        await ReadSelectedUserNameByClipboardAsync(
                            current,
                            cancellationToken);

                    if (string.IsNullOrWhiteSpace(currentUser))
                    {
                        currentUser =
                            await ReadSelectedUserNameAsync(
                                current,
                                cancellationToken);
                    }
                }

                var currentHash =
                    CaptureUserFieldFingerprint(
                        current.AnchorX,
                        current.AnchorY);

                var safeName =
                    string.IsNullOrWhiteSpace(currentUser)
                        ? string.Empty
                        : WorkflowName.Sanitize(currentUser);

                var alreadyReviewedByName =
                    !string.IsNullOrWhiteSpace(safeName) &&
                    reviewedNames.Contains(safeName);

                var alreadyReviewedByHash =
                    reviewedHashes.Contains(currentHash);

                // Si tenemos un ComboBox enumerado, el índice es la fuente de verdad:
                // se procesa 0..Count-1 SIN omitir ninguno, aunque una captura visual
                // produzca una huella repetida por retraso de repintado.
                // En el fallback visual, la huella solo se usa si NO logramos leer nombre;
                // dos usuarios distintos pueden tener una captura muy parecida.
                var alreadyReviewed =
                    comboCount <= 0 &&
                    (alreadyReviewedByName ||
                     (string.IsNullOrWhiteSpace(safeName) && alreadyReviewedByHash));

                if (alreadyReviewed)
                {
                    consecutiveDuplicates++;

                    Console.WriteLine(
                        $"[USUARIOS] Selección repetida en fallback. " +
                        $"Nombre={(string.IsNullOrWhiteSpace(safeName) ? "(no legible)" : safeName)} " +
                        $"Huella=0x{currentHash:X16}. Se omite esta repetición.");

                    continue;
                }

                consecutiveDuplicates = 0;

                reviewedHashes.Add(
                    currentHash);

                if (!string.IsNullOrWhiteSpace(safeName))
                {
                    reviewedNames.Add(
                        safeName);
                }

                Console.WriteLine();
                Console.WriteLine(
                    $"=== USUARIO #{usersChecked + 1} / " +
                    $"{(comboCount > 0 ? comboCount.ToString() : "?")} ===");

                if (!string.IsNullOrWhiteSpace(safeName))
                {
                    Console.WriteLine(
                        $"[USUARIO] Seleccionado: {safeName}");
                }
                else
                {
                    Console.WriteLine(
                        $"[USUARIO] Nombre no legible; huella=0x{currentHash:X16}");
                }

                usersChecked++;

                await RequireMonthViewAsync(
                    dateStep,
                    cancellationToken);

                await EnsureYesterdaySelectedAsync(
                    dateStep,
                    cancellationToken);

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
                        "RESULTADO: HAY CIERRE.");

                    var initialBaseName =
                        !string.IsNullOrWhiteSpace(safeName)
                            ? $"CIERRE_{reportDate:yyyy-MM-dd}_{safeName}"
                            : $"CIERRE_{reportDate:yyyy-MM-dd}_TEMP_{ordinal + 1:00}";

                    var savedPath =
                        await SaveClosureDialogAsync(
                            saveDialog,
                            outputDirectory,
                            initialBaseName,
                            cancellationToken);

                    string finalPath = savedPath;

                    if (!string.IsNullOrWhiteSpace(safeName))
                    {
                        Console.WriteLine(
                            $"[USUARIO] Nombre usado para archivo: {safeName}");
                    }
                    else
                    {
                        var reportUser =
                            await TryReadUserFromXlsxAsync(
                                savedPath,
                                cancellationToken);

                        if (!string.IsNullOrWhiteSpace(reportUser))
                        {
                            var safeReportUser =
                                WorkflowName.Sanitize(reportUser);

                            Console.WriteLine(
                                $"[USUARIO] Nombre leído DEL REPORTE: {safeReportUser}");

                            finalPath =
                                FinalizeClosureFileName(
                                    savedPath,
                                    outputDirectory,
                                    reportDate,
                                    safeReportUser);
                        }
                        else
                        {
                            var fallback =
                                $"USUARIO_{ordinal + 1:00}";

                            Console.WriteLine(
                                $"[USUARIO] No se pudo recuperar el nombre; fallback={fallback}");

                            finalPath =
                                FinalizeClosureFileName(
                                    savedPath,
                                    outputDirectory,
                                    reportDate,
                                    fallback);
                        }
                    }

                    filesSaved++;

                    Console.WriteLine(
                        $"[GUARDADO] Archivo final: {finalPath}");

                    await CloseNewExcelProcessesAsync(
                        excelPidsBeforeRun,
                        cancellationToken);

                    await Task.Delay(
                        700,
                        cancellationToken);
                }
                else
                {
                    Console.WriteLine(
                        "RESULTADO: SIN CIERRE.");

                    await CloseAnyDialogAsync(
                        workflow.TargetProcessName,
                        cancellationToken);
                }

                await RequireMonthViewAsync(
                    dateStep,
                    cancellationToken);
            }

            Console.WriteLine();
            Console.WriteLine("=== RESUMEN V387 ===");
            Console.WriteLine(
                $"Usuarios revisados en orden: {usersChecked}");
            Console.WriteLine(
                $"Cierres detectados: {closuresFound}");
            Console.WriteLine(
                $"Archivos guardados: {filesSaved}");
            Console.WriteLine(
                $"Destino: {outputDirectory}");
        }
        finally
        {
            Console.WriteLine();
            Console.WriteLine("[LIMPIEZA] Cerrando Excel generado por el bot...");

            await CloseNewExcelProcessesAsync(
                excelPidsBeforeRun,
                CancellationToken.None);

            Console.WriteLine("[LIMPIEZA] Cerrando SoftRestaurant...");

            await CloseSoftRestaurantAsync(
                workflow.TargetProcessName,
                CancellationToken.None);
        }
    }

    private readonly record struct SelectionResult(
        IntPtr Window,
        int AnchorX,
        int AnchorY);

    private readonly record struct NextUserResult(
        SelectionResult Selection,
        ulong Hash,
        string UserName);

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

    private static IntPtr FindUserComboHandle(
        SelectionResult selection)
    {
        var point =
            new NativeMethods.POINT
            {
                X = selection.AnchorX,
                Y = selection.AnchorY
            };

        var direct =
            NativeMethods.WindowFromPoint(
                point);

        var candidates =
            new List<IntPtr>();

        if (direct != IntPtr.Zero)
        {
            candidates.Add(direct);

            var parent = direct;

            for (var i = 0;
                 i < 6 &&
                 parent != IntPtr.Zero;
                 i++)
            {
                parent =
                    NativeMethods.GetParent(
                        parent);

                if (parent != IntPtr.Zero &&
                    !candidates.Contains(parent))
                {
                    candidates.Add(parent);
                }
            }
        }

        NativeMethods.EnumChildWindows(
            selection.Window,
            (child, _) =>
            {
                if (!NativeMethods.IsWindowVisible(child) ||
                    !NativeMethods.IsWindowEnabled(child))
                {
                    return true;
                }

                if (!NativeMethods.GetWindowRect(
                        child,
                        out var rect))
                {
                    return true;
                }

                var near =
                    selection.AnchorX >= rect.Left - 20 &&
                    selection.AnchorX <= rect.Right + 20 &&
                    selection.AnchorY >= rect.Top - 20 &&
                    selection.AnchorY <= rect.Bottom + 20;

                if (!near)
                    return true;

                var className =
                    GetClassName(
                        child);

                if (className.Contains(
                        "Combo",
                        StringComparison.OrdinalIgnoreCase) ||
                    className.Contains(
                        "Thunder",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!candidates.Contains(child))
                        candidates.Add(child);
                }

                return true;
            },
            IntPtr.Zero);

        foreach (var candidate in candidates)
        {
            var count =
                GetComboItemCount(
                    candidate);

            if (count > 0 &&
                count <= MaxUsers)
            {
                Console.WriteLine(
                    $"[USUARIOS] Candidato ComboBox: " +
                    $"HWND=0x{candidate.ToInt64():X} " +
                    $"Class=\"{GetClassName(candidate)}\" Count={count}");

                return candidate;
            }
        }

        return IntPtr.Zero;
    }

    private static int GetComboItemCount(
        IntPtr combo)
    {
        if (combo == IntPtr.Zero)
            return -1;

        try
        {
            var result =
                NativeMethods.SendMessage(
                    combo,
                    NativeMethods.CB_GETCOUNT,
                    IntPtr.Zero,
                    IntPtr.Zero)
                .ToInt32();

            return result == NativeMethods.CB_ERR
                ? -1
                : result;
        }
        catch
        {
            return -1;
        }
    }

    private static async Task<SelectionResult> SelectComboUserByIndexAsync(
        WorkflowStep anchor,
        IntPtr combo,
        int index,
        CancellationToken cancellationToken)
    {
        var selection =
            await FocusUserSelectorAsync(
                anchor,
                cancellationToken);

        SendKey(
            0x1B, // ESC
            false,
            false,
            false);

        await Task.Delay(
            120,
            cancellationToken);

        var selected =
            NativeMethods.SendMessage(
                combo,
                NativeMethods.CB_SETCURSEL,
                new IntPtr(index),
                IntPtr.Zero)
            .ToInt32();

        if (selected == NativeMethods.CB_ERR)
        {
            throw new InvalidOperationException(
                $"SoftRestaurant rechazó el usuario índice {index}.");
        }

        var parent =
            NativeMethods.GetParent(
                combo);

        if (parent != IntPtr.Zero)
        {
            var controlId =
                NativeMethods.GetDlgCtrlID(
                    combo);

            var wParam =
                new IntPtr(
                    (controlId & 0xFFFF) |
                    ((NativeMethods.CBN_SELCHANGE & 0xFFFF) << 16));

            NativeMethods.SendMessage(
                parent,
                NativeMethods.WM_COMMAND,
                wParam,
                combo);
        }

        NativeMethods.SetFocus(
            combo);

        await Task.Delay(
            450,
            cancellationToken);

        var actualIndex =
            NativeMethods.SendMessage(
                combo,
                NativeMethods.CB_GETCURSEL,
                IntPtr.Zero,
                IntPtr.Zero)
            .ToInt32();

        if (actualIndex != index)
        {
            // Un segundo intento controlado evita continuar con una selección vieja.
            NativeMethods.SendMessage(
                combo,
                NativeMethods.CB_SETCURSEL,
                new IntPtr(index),
                IntPtr.Zero);

            if (parent != IntPtr.Zero)
            {
                var controlId =
                    NativeMethods.GetDlgCtrlID(
                        combo);

                var wParam =
                    new IntPtr(
                        (controlId & 0xFFFF) |
                        ((NativeMethods.CBN_SELCHANGE & 0xFFFF) << 16));

                NativeMethods.SendMessage(
                    parent,
                    NativeMethods.WM_COMMAND,
                    wParam,
                    combo);
            }

            await Task.Delay(
                450,
                cancellationToken);

            actualIndex =
                NativeMethods.SendMessage(
                    combo,
                    NativeMethods.CB_GETCURSEL,
                    IntPtr.Zero,
                    IntPtr.Zero)
                .ToInt32();
        }

        if (actualIndex != index)
        {
            throw new InvalidOperationException(
                $"No se pudo fijar exactamente el usuario #{index + 1}. " +
                $"Índice solicitado={index}; índice real={actualIndex}.");
        }

        var exactText =
            GetComboItemText(
                combo,
                index);

        Console.WriteLine(
            $"[USUARIOS] Índice exacto confirmado: {index + 1}/{GetComboItemCount(combo)} " +
            $"-> {(string.IsNullOrWhiteSpace(exactText) ? "(sin texto)" : exactText)}.");

        return selection;
    }

    private static async Task<SelectionResult> SelectUserByAbsoluteIndexAsync(
        WorkflowStep anchor,
        int index,
        CancellationToken cancellationToken)
    {
        // V3.8.7 fallback estricto:
        // el wheel de Windows NO equivale a una fila; normalmente una muesca puede
        // desplazar 3 líneas y por eso se podían brincar usuarios.
        // Aquí cada selección siempre parte del inicio con HOME y avanza exactamente
        // un usuario por cada VK_DOWN. Finalmente ENTER confirma la fila.

        var selection =
            await FocusUserSelectorAsync(
                anchor,
                cancellationToken);

        NativeMethods.SetForegroundWindow(
            selection.Window);

        await Task.Delay(
            180,
            cancellationToken);

        Console.WriteLine(
            $"[USUARIOS] Fallback exacto: HOME + DOWN x{index} + ENTER -> usuario #{index + 1}.");

        SendKey(
            0x24, // VK_HOME
            false,
            false,
            false);

        await Task.Delay(
            180,
            cancellationToken);

        for (var i = 0; i < index; i++)
        {
            SendKey(
                0x28, // VK_DOWN
                false,
                false,
                false);

            // Pausa corta para que el ComboBox VB6 no pierda eventos cuando
            // se mandan varias flechas seguidas.
            await Task.Delay(
                55,
                cancellationToken);
        }

        SendKey(
            0x0D, // ENTER
            false,
            false,
            false);

        await Task.Delay(
            500,
            cancellationToken);

        return selection;
    }

    private static async Task<string> ReadSelectedUserNameByClipboardAsync(
        SelectionResult selection,
        CancellationToken cancellationToken)
    {
        // En SoftRestaurant/VB6 el texto visible del ComboBox puede no exponerse
        // por WM_GETTEXT/MSAA. Sin embargo, al enfocar la parte de texto del
        // selector, Ctrl+C copia literalmente el valor visible (ej. DURAN).
        var textX =
            selection.AnchorX +
            InsideListOffsetX;

        var textY =
            selection.AnchorY;

        NativeMethods.SetForegroundWindow(
            selection.Window);

        NativeMethods.SetCursorPos(
            textX,
            textY);

        Click();

        await Task.Delay(
            120,
            cancellationToken);

        ClipboardHelper.TryClear();

        SendKey(
            0x43, // C
            true,
            false,
            false);

        await Task.Delay(
            180,
            cancellationToken);

        var copied =
            ClipboardHelper.GetText()
                .Trim();

        if (IsPlausibleUserName(copied))
        {
            var normalized =
                WorkflowName.Sanitize(copied);

            Console.WriteLine(
                $"[USUARIO] Clipboard directo -> {normalized}");

            return normalized;
        }

        // Fallback para combos editables: seleccionar el texto y copiar.
        SendKey(
            NativeMethods.VK_A,
            true,
            false,
            false);

        await Task.Delay(
            80,
            cancellationToken);

        ClipboardHelper.TryClear();

        SendKey(
            0x43, // C
            true,
            false,
            false);

        await Task.Delay(
            180,
            cancellationToken);

        copied =
            ClipboardHelper.GetText()
                .Trim();

        if (IsPlausibleUserName(copied))
        {
            var normalized =
                WorkflowName.Sanitize(copied);

            Console.WriteLine(
                $"[USUARIO] Clipboard Ctrl+A/Ctrl+C -> {normalized}");

            return normalized;
        }

        Console.WriteLine(
            "[USUARIO] Clipboard no devolvió un nombre.");

        return string.Empty;
    }

    private static async Task<SelectionResult> SelectFirstUserAsync(
        WorkflowStep anchor,
        CancellationToken cancellationToken)
    {
        var selection =
            await FocusUserSelectorAsync(
                anchor,
                cancellationToken);

        Console.WriteLine(
            "[USUARIOS] Inicio ordenado: HOME -> ENTER (primer usuario).");

        SendKey(
            0x24, // VK_HOME
            false,
            false,
            false);

        await Task.Delay(
            250,
            cancellationToken);

        SendKey(
            0x0D, // ENTER
            false,
            false,
            false);

        await Task.Delay(
            500,
            cancellationToken);

        return selection;
    }

    private static async Task<NextUserResult?> SelectNextUserOneStepAsync(
        WorkflowStep anchor,
        string previousUserName,
        ulong previousHash,
        CancellationToken cancellationToken)
    {
        var selection =
            await FocusUserSelectorAsync(
                anchor,
                cancellationToken);

        Console.WriteLine(
            "[USUARIOS] Avance ordenado: FLECHA ABAJO x1 -> ENTER.");

        SendKey(
            0x28, // VK_DOWN
            false,
            false,
            false);

        await Task.Delay(
            250,
            cancellationToken);

        SendKey(
            0x0D, // ENTER
            false,
            false,
            false);

        await Task.Delay(
            450,
            cancellationToken);

        var userName =
            await ReadSelectedUserNameAsync(
                selection,
                cancellationToken);

        var hash =
            CaptureUserFieldFingerprint(
                selection.AnchorX,
                selection.AnchorY);

        if (!string.IsNullOrWhiteSpace(userName))
        {
            Console.WriteLine(
                $"[USUARIO] Después de DOWN: {userName}");

            if (!string.IsNullOrWhiteSpace(previousUserName) &&
                string.Equals(
                    userName,
                    previousUserName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }
        else
        {
            Console.WriteLine(
                $"[USUARIOS] Nombre no legible; huella después de DOWN: 0x{hash:X16}");

            if (hash == previousHash)
                return null;
        }

        return new NextUserResult(
            selection,
            hash,
            userName);
    }

    private static async Task<SelectionResult> FocusUserSelectorAsync(
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
                "No apareció el selector Usuario.");
        }

        if (!NativeMethods.GetWindowRect(
                window,
                out var rect))
        {
            throw new InvalidOperationException(
                "No se pudo leer el selector Usuario.");
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

        NativeMethods.SetForegroundWindow(window);
        NativeMethods.SetCursorPos(anchorX, anchorY);
        Click();

        await Task.Delay(
            350,
            cancellationToken);

        return new SelectionResult(
            window,
            anchorX,
            anchorY);
    }

    private static async Task<string> ReadSelectedUserNameAsync(
        SelectionResult selection,
        CancellationToken cancellationToken)
    {
        // Damos un margen corto para que el ComboBox termine de reflejar
        // la selección después de HOME/DOWN + ENTER.
        await Task.Delay(
            120,
            cancellationToken);

        var point =
            new NativeMethods.POINT
            {
                X = selection.AnchorX,
                Y = selection.AnchorY
            };

        var candidates =
            new List<IntPtr>();

        // 1) El HWND directamente bajo el punto entrenado.
        var direct =
            NativeMethods.WindowFromPoint(point);

        if (direct != IntPtr.Zero)
            candidates.Add(direct);

        // 2) Subir por la jerarquía: en VB6 el punto puede caer en el Edit hijo
        // del ComboBox y el padre real es ThunderRT6ComboBox/ComboBox.
        var parent = direct;

        for (var i = 0; i < 5 && parent != IntPtr.Zero; i++)
        {
            parent = NativeMethods.GetParent(parent);

            if (parent != IntPtr.Zero &&
                !candidates.Contains(parent))
            {
                candidates.Add(parent);
            }
        }

        // 3) Enumerar controles hijos cercanos al punto del selector.
        NativeMethods.EnumChildWindows(
            selection.Window,
            (child, _) =>
            {
                if (!NativeMethods.IsWindowVisible(child) ||
                    !NativeMethods.IsWindowEnabled(child))
                {
                    return true;
                }

                if (!NativeMethods.GetWindowRect(
                        child,
                        out var rect))
                {
                    return true;
                }

                var near =
                    selection.AnchorX >= rect.Left - 12 &&
                    selection.AnchorX <= rect.Right + 12 &&
                    selection.AnchorY >= rect.Top - 12 &&
                    selection.AnchorY <= rect.Bottom + 12;

                if (!near)
                    return true;

                var className =
                    GetClassName(child);

                if (className.Contains(
                        "Combo",
                        StringComparison.OrdinalIgnoreCase) ||
                    className.Contains(
                        "Edit",
                        StringComparison.OrdinalIgnoreCase) ||
                    className.Contains(
                        "Thunder",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!candidates.Contains(child))
                        candidates.Add(child);
                }

                return true;
            },
            IntPtr.Zero);

        foreach (var candidate in candidates)
        {
            var className =
                GetClassName(candidate);

            // Preferencia A: mensajes propios de ComboBox.
            var comboText =
                TryReadComboSelectedText(candidate);

            if (IsPlausibleUserName(comboText))
            {
                var normalized =
                    WorkflowName.Sanitize(comboText);

                Console.WriteLine(
                    $"[USUARIO] Combo HWND=0x{candidate.ToInt64():X} " +
                    $"Class=\"{className}\" -> {normalized}");

                return normalized;
            }

            // Preferencia B: WM_GETTEXT/GetWindowText del control o Edit hijo.
            var windowText =
                GetWindowText(candidate).Trim();

            if (IsPlausibleUserName(windowText))
            {
                var normalized =
                    WorkflowName.Sanitize(windowText);

                Console.WriteLine(
                    $"[USUARIO] Texto HWND=0x{candidate.ToInt64():X} " +
                    $"Class=\"{className}\" -> {normalized}");

                return normalized;
            }

            // Preferencia C: MSAA dirigido SOLO al control candidato.
            try
            {
                var accessible =
                    LegacyAccessibleReader.ReadTree(
                        candidate,
                        source: "selector-usuario",
                        maxDepth: 4,
                        maxNodes: 80);

                var accessibleUser =
                    LegacyAccessibleReader
                        .ExtractCandidateUsers(accessible)
                        .FirstOrDefault(IsPlausibleUserName);

                if (!string.IsNullOrWhiteSpace(accessibleUser))
                {
                    var normalized =
                        WorkflowName.Sanitize(accessibleUser);

                    Console.WriteLine(
                        $"[USUARIO] MSAA HWND=0x{candidate.ToInt64():X} " +
                        $"Class=\"{className}\" -> {normalized}");

                    return normalized;
                }
            }
            catch
            {
                // Continuar con el siguiente candidato.
            }
        }

        Console.WriteLine(
            $"[USUARIO] No se logró leer el ComboBox en ({selection.AnchorX},{selection.AnchorY}).");

        return string.Empty;
    }

    private static string GetComboItemText(
        IntPtr combo,
        int index)
    {
        if (combo == IntPtr.Zero || index < 0)
            return string.Empty;

        try
        {
            var length =
                NativeMethods.SendMessage(
                    combo,
                    NativeMethods.CB_GETLBTEXTLEN,
                    new IntPtr(index),
                    IntPtr.Zero)
                .ToInt32();

            if (length <= 0 || length > 256)
                return string.Empty;

            var buffer =
                new StringBuilder(length + 2);

            var copied =
                NativeMethods.SendMessage(
                    combo,
                    NativeMethods.CB_GETLBTEXT,
                    new IntPtr(index),
                    buffer)
                .ToInt32();

            if (copied == NativeMethods.CB_ERR || copied <= 0)
                return string.Empty;

            return buffer.ToString().Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TryReadComboSelectedText(
        IntPtr combo)
    {
        if (combo == IntPtr.Zero)
            return string.Empty;

        try
        {
            var selected =
                NativeMethods.SendMessage(
                    combo,
                    NativeMethods.CB_GETCURSEL,
                    IntPtr.Zero,
                    IntPtr.Zero)
                .ToInt32();

            if (selected == NativeMethods.CB_ERR ||
                selected < 0)
            {
                return string.Empty;
            }

            var length =
                NativeMethods.SendMessage(
                    combo,
                    NativeMethods.CB_GETLBTEXTLEN,
                    new IntPtr(selected),
                    IntPtr.Zero)
                .ToInt32();

            if (length <= 0 || length > 256)
                return string.Empty;

            var buffer =
                new StringBuilder(length + 2);

            var copied =
                NativeMethods.SendMessage(
                    combo,
                    NativeMethods.CB_GETLBTEXT,
                    new IntPtr(selected),
                    buffer)
                .ToInt32();

            if (copied == NativeMethods.CB_ERR || copied <= 0)
                return string.Empty;

            return buffer.ToString().Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsPlausibleUserName(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text =
            value.Trim();

        if (text.Length < 2 || text.Length > 60)
            return false;

        var blocked =
            new[]
            {
                "USUARIO",
                "USUARIOS",
                "FECHA",
                "DESDE",
                "HASTA",
                "ACEPTAR",
                "CANCELAR",
                "EXCEL",
                "VISTA PREVIA",
                "SOFT RESTAURANT",
                "FORMAS DE PAGO",
                "FORMAS DE PAGO POR TURNO"
            };

        if (blocked.Any(x =>
                text.Equals(
                    x,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (text.Any(char.IsDigit))
            return false;

        return text.All(ch =>
            char.IsLetter(ch) ||
            char.IsWhiteSpace(ch) ||
            ch == '-' ||
            ch == '_' ||
            ch == '.');
    }

    private static async Task<string> TryReadUserFromXlsxAsync(
        string path,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var user =
                    TryReadUserFromXlsx(path);

                if (!string.IsNullOrWhiteSpace(user))
                {
                    return user;
                }
            }
            catch (IOException)
            {
                // Excel/OneDrive puede mantener el archivo ocupado unos instantes.
            }
            catch (InvalidDataException)
            {
                // El ZIP puede no estar completamente finalizado todavía.
            }

            await Task.Delay(
                350,
                cancellationToken);
        }

        return string.Empty;
    }

    private static string TryReadUserFromXlsx(
        string path)
    {
        if (!File.Exists(path))
            return string.Empty;

        var extension =
            Path.GetExtension(path);

        if (!extension.Equals(
                ".xlsx",
                StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        using var stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

        using var archive =
            new ZipArchive(
                stream,
                ZipArchiveMode.Read,
                leaveOpen: false);

        var fragments =
            new List<string>();

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.Equals(
                    "xl/sharedStrings.xml",
                    StringComparison.OrdinalIgnoreCase) &&
                !entry.FullName.StartsWith(
                    "xl/worksheets/",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!entry.FullName.EndsWith(
                    ".xml",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var entryStream = entry.Open();
            var document = XDocument.Load(entryStream);

            fragments.AddRange(
                document
                    .DescendantNodes()
                    .OfType<XText>()
                    .Select(x => x.Value)
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        var text =
            string.Join(
                " ",
                fragments);

        // Ejemplo real del reporte:
        // "... CAJA - USUARIO: DURAN, TURNO: 1"
        var match =
            Regex.Match(
                text,
                @"USUARIO\s*:\s*([^,;\r\n]{2,60})",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant);

        if (!match.Success)
            return string.Empty;

        var candidate =
            match.Groups[1].Value.Trim();

        // Evitar que una concatenación de XML se lleve "TURNO" u otro campo.
        candidate =
            Regex.Replace(
                candidate,
                @"\s+TURNO\s*:.*$",
                string.Empty,
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant)
            .Trim();

        return candidate;
    }

    private static string FinalizeClosureFileName(
        string temporaryPath,
        string outputDirectory,
        DateTime reportDate,
        string userName)
    {
        var safeUser =
            WorkflowName.Sanitize(userName);

        if (string.IsNullOrWhiteSpace(safeUser))
            safeUser = "USUARIO_DESCONOCIDO";

        var extension =
            Path.GetExtension(temporaryPath);

        if (string.IsNullOrWhiteSpace(extension))
            extension = ".xlsx";

        var finalPath =
            Path.Combine(
                outputDirectory,
                $"CIERRE_{reportDate:yyyy-MM-dd}_{safeUser}{extension}");

        if (string.Equals(
                temporaryPath,
                finalPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return finalPath;
        }

        if (File.Exists(finalPath))
        {
            Console.WriteLine(
                $"[GUARDADO] Ya existía {Path.GetFileName(finalPath)}; no se creará duplicado.");

            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
            }

            return finalPath;
        }

        File.Move(
            temporaryPath,
            finalPath);

        return finalPath;
    }

    private static async Task CloseExcelForFileAsync(
        string filePath,
        HashSet<int> excelPidsBeforeRun,
        CancellationToken cancellationToken)
    {
        var fileStem =
            Path.GetFileNameWithoutExtension(filePath);

        // Primero cerrar sólo una ventana de Excel cuyo título corresponda
        // al archivo generado. Esto también funciona si Excel reutilizó un PID
        // que ya existía antes del bot.
        var matchingWindows =
            new List<IntPtr>();

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
                    using var process =
                        Process.GetProcessById((int)pid);

                    if (!process.ProcessName.Equals(
                            "EXCEL",
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
                    GetWindowText(hWnd);

                if (title.Contains(
                        fileStem,
                        StringComparison.OrdinalIgnoreCase))
                {
                    matchingWindows.Add(hWnd);
                }

                return true;
            },
            IntPtr.Zero);

        foreach (var hWnd in matchingWindows)
        {
            NativeMethods.SendMessage(
                hWnd,
                NativeMethods.WM_CLOSE,
                IntPtr.Zero,
                IntPtr.Zero);
        }

        if (matchingWindows.Count > 0)
        {
            Console.WriteLine(
                $"[EXCEL] Cerrada ventana del cierre {Path.GetFileName(filePath)}.");
        }

        await Task.Delay(
            700,
            cancellationToken);

        await CloseNewExcelProcessesAsync(
            excelPidsBeforeRun,
            cancellationToken);
    }

    private static async Task CloseNewExcelProcessesAsync(
        HashSet<int> excelPidsBeforeRun,
        CancellationToken cancellationToken)
    {
        foreach (var process in Process.GetProcessesByName("EXCEL"))
        {
            using (process)
            {
                if (excelPidsBeforeRun.Contains(process.Id))
                    continue;

                try
                {
                    Console.WriteLine(
                        $"[EXCEL] Cerrando instancia creada por el bot. PID={process.Id}");

                    process.CloseMainWindow();

                    var exited =
                        await Task.Run(
                            () => process.WaitForExit(2500),
                            cancellationToken);

                    if (!exited && !process.HasExited)
                    {
                        process.Kill(
                            entireProcessTree: true);

                        await Task.Run(
                            () => process.WaitForExit(2000),
                            cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[EXCEL] No se pudo cerrar PID={process.Id}: {ex.Message}");
                }
            }
        }
    }

    private static async Task CloseSoftRestaurantAsync(
        string processName,
        CancellationToken cancellationToken)
    {
        var processes =
            Process.GetProcesses()
                .Where(
                    p => p.ProcessName.Equals(
                        processName,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    var windows =
                        WindowInfo.GetVisibleWindowsForProcess(
                            processName)
                            .Where(x =>
                            {
                                NativeMethods.GetWindowThreadProcessId(
                                    x.Handle,
                                    out var pid);

                                return pid == process.Id;
                            })
                            .ToList();

                    foreach (var window in windows)
                    {
                        NativeMethods.SendMessage(
                            window.Handle,
                            NativeMethods.WM_CLOSE,
                            IntPtr.Zero,
                            IntPtr.Zero);
                    }

                    await Task.Delay(
                        800,
                        cancellationToken);

                    await AcceptExitConfirmationAsync(
                        processName,
                        cancellationToken);

                    var exited =
                        await Task.Run(
                            () => process.WaitForExit(3500),
                            cancellationToken);

                    if (!exited && !process.HasExited)
                    {
                        Console.WriteLine(
                            $"[LIMPIEZA] SoftRestaurant PID={process.Id} no cerró; cierre forzado.");

                        process.Kill(
                            entireProcessTree: true);

                        await Task.Run(
                            () => process.WaitForExit(2000),
                            cancellationToken);
                    }
                    else
                    {
                        Console.WriteLine(
                            $"[LIMPIEZA] SoftRestaurant PID={process.Id} cerrado.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[LIMPIEZA] Error cerrando SoftRestaurant PID={process.Id}: {ex.Message}");
                }
            }
        }
    }

    private static async Task AcceptExitConfirmationAsync(
        string processName,
        CancellationToken cancellationToken)
    {
        IntPtr dialog = IntPtr.Zero;

        NativeMethods.EnumWindows(
            (hWnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd))
                    return true;

                var snapshot =
                    WindowInfo.GetSnapshot(hWnd);

                if (!snapshot.ProcessName.Equals(
                        processName,
                        StringComparison.OrdinalIgnoreCase) ||
                    !snapshot.ClassName.Equals(
                        "#32770",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                dialog = hWnd;
                return false;
            },
            IntPtr.Zero);

        if (dialog == IntPtr.Zero)
            return;

        IntPtr acceptButton = IntPtr.Zero;

        NativeMethods.EnumChildWindows(
            dialog,
            (child, _) =>
            {
                var cls = GetClassName(child);
                var text = GetWindowText(child).Trim();

                if (!cls.Equals(
                        "Button",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (text.Equals("Sí", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("Si", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("Aceptar", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("Yes", StringComparison.OrdinalIgnoreCase))
                {
                    acceptButton = child;
                    return false;
                }

                return true;
            },
            IntPtr.Zero);

        if (acceptButton != IntPtr.Zero)
        {
            NativeMethods.SendMessage(
                acceptButton,
                NativeMethods.BM_CLICK,
                IntPtr.Zero,
                IntPtr.Zero);

            await Task.Delay(
                500,
                cancellationToken);
        }
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

        var y =
            rect.Top +
            (int)Math.Round(
                rect.Height * top +
                ch * (row + 0.5));

        Console.WriteLine(
            $"Seleccionando AYER {target:dd/MM/yyyy} click=({x},{y})");

        NativeMethods.SetForegroundWindow(
            month);

        NativeMethods.SetCursorPos(
            x,
            y);

        Click();

        await Task.Delay(
            500,
            cancellationToken);
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