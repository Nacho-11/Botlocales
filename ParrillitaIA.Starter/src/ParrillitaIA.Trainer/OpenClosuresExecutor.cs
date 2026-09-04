param(
    [string]$RepoRoot = "."
)

$ErrorActionPreference = "Stop"

$trainer = Join-Path $RepoRoot "ParrillitaIA.Starter\src\ParrillitaIA.Trainer"
$runner = Join-Path $trainer "WorkflowRunner.cs"
$newExecutor = Join-Path $trainer "OpenClosuresExecutor.cs"
$solution = Join-Path $RepoRoot "ParrillitaIA.Starter\ParrillitaIA.sln"

if (-not (Test-Path $runner)) {
    throw "No se encontró WorkflowRunner.cs en: $runner"
}

$executorSource = @'
using System.Text;

namespace ParrillitaIA.Trainer;

internal static class OpenClosuresExecutor
{
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
                out var rect))
        {
            throw new InvalidOperationException(
                "OPEN_CIERRES: no se pudo leer la geometría.");
        }

        NativeMethods.SetForegroundWindow(
            mainWindow);

        await Task.Delay(
            350,
            cancellationToken);

        var first =
            ToPoint(
                rect,
                clicks[0]);

        var second =
            ToPoint(
                rect,
                clicks[1]);

        var before =
            DescribePoint(
                second);

        Console.WriteLine();
        Console.WriteLine(
            "[OPEN] Inicio apertura controlada");

        Console.WriteLine(
            $"[OPEN] Principal: {Describe(mainWindow)}");

        Console.WriteLine(
            $"[OPEN] Paso 1 Reportes: ({first.X},{first.Y})");

        Console.WriteLine(
            $"[OPEN] Punto 2 antes del clic 1: {before}");

        ClickAt(
            first);

        var changed =
            await WaitForTargetChangeAsync(
                second,
                before.Handle,
                3000,
                cancellationToken);

        Console.WriteLine(
            $"[OPEN] Después del clic 1: {changed.Description}");

        if (!changed.Changed)
        {
            throw new InvalidOperationException(
                "OPEN_CIERRES: Reportes no produjo un cambio detectable " +
                "en la zona de Formas de pago por turno.");
        }

        Console.WriteLine(
            $"[OPEN] Paso 2 Formas de pago por turno: ({second.X},{second.Y})");

        // No reenfocar aquí la ventana principal.
        ClickAt(
            second);

        var monthView =
            await SoftRestaurantReportContext
                .WaitForVisibleMonthViewAsync(
                    workflow.TargetProcessName,
                    mainWindow,
                    15000,
                    cancellationToken);

        if (monthView == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "OPEN_CIERRES: el menú reaccionó al primer clic, " +
                "pero el segundo clic no abrió el formulario.");
        }

        Console.WriteLine(
            $"[OPEN] Formulario abierto. MonthView={Describe(monthView)}");
    }

    private static NativeMethods.POINT ToPoint(
        NativeMethods.RECT rect,
        WorkflowStep step) =>
        new()
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

    private static async Task<Observation> WaitForTargetChangeAsync(
        NativeMethods.POINT point,
        IntPtr previous,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var deadline =
            DateTimeOffset.UtcNow.AddMilliseconds(
                timeoutMs);

        PointTarget last =
            DescribePoint(
                point);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            last =
                DescribePoint(
                    point);

            var isPopupMenu =
                last.ClassName.Equals(
                    "#32768",
                    StringComparison.OrdinalIgnoreCase);

            if (last.Handle != IntPtr.Zero &&
                (last.Handle != previous ||
                 isPopupMenu))
            {
                await Task.Delay(
                    100,
                    cancellationToken);

                var confirm =
                    DescribePoint(
                        point);

                if (confirm.Handle == last.Handle ||
                    confirm.ClassName.Equals(
                        "#32768",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new Observation(
                        true,
                        confirm.ToString());
                }
            }

            await Task.Delay(
                75,
                cancellationToken);
        }

        return new Observation(
            false,
            last.ToString());
    }

    private static PointTarget DescribePoint(
        NativeMethods.POINT point)
    {
        var hwnd =
            NativeMethods.WindowFromPoint(
                point);

        if (hwnd == IntPtr.Zero)
        {
            return new PointTarget(
                IntPtr.Zero,
                "",
                "");
        }

        return new PointTarget(
            hwnd,
            GetClassName(hwnd),
            GetWindowText(hwnd));
    }

    private static void ClickAt(
        NativeMethods.POINT point)
    {
        if (!NativeMethods.SetCursorPos(
                point.X,
                point.Y))
        {
            throw new InvalidOperationException(
                $"No se pudo mover el cursor a ({point.X},{point.Y}).");
        }

        var inputs =
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
            };

        var sent =
            NativeMethods.SendInput(
                (uint)inputs.Length,
                inputs,
                System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());

        if (sent != inputs.Length)
        {
            throw new InvalidOperationException(
                $"SendInput {sent}/{inputs.Length}");
        }
    }

    private static string Describe(
        IntPtr hwnd) =>
        $"HWND=0x{hwnd.ToInt64():X} " +
        $"Class=\"{GetClassName(hwnd)}\" " +
        $"Text=\"{GetWindowText(hwnd)}\"";

    private static string GetClassName(
        IntPtr hwnd)
    {
        var sb =
            new StringBuilder(
                256);

        NativeMethods.GetClassName(
            hwnd,
            sb,
            sb.Capacity);

        return sb.ToString();
    }

    private static string GetWindowText(
        IntPtr hwnd)
    {
        var sb =
            new StringBuilder(
                512);

        NativeMethods.GetWindowText(
            hwnd,
            sb,
            sb.Capacity);

        return sb.ToString().Trim();
    }

    private readonly record struct PointTarget(
        IntPtr Handle,
        string ClassName,
        string Text)
    {
        public override string ToString() =>
            $"HWND=0x{Handle.ToInt64():X} " +
            $"Class=\"{ClassName}\" Text=\"{Text}\"";
    }

    private readonly record struct Observation(
        bool Changed,
        string Description);
}
'@

Set-Content -Path $newExecutor -Value $executorSource -Encoding UTF8
Write-Host "[OK] OpenClosuresExecutor.cs creado."

$runnerText = Get-Content -Path $runner -Raw

$oldBlock = @'
            // V6.18.7:
            // Reproducir OPEN_CIERRES con EXACTAMENTE el mismo motor genérico
            // usado para cualquier entrenamiento normal. Sin implementación
            // especial de clics, foco, delays o resolución de ventana.
            await new WorkflowRunner()
                .RunAsync(
                    openClosuresWorkflow,
                    cancellationToken);
'@

$newBlock = @'
            // V6.19:
            // OPEN_CIERRES se ejecuta por estado de UI.
            // El segundo clic solo se realiza si el primer clic produjo
            // un cambio real en la ventana/control bajo la opción.
            await OpenClosuresExecutor.RunAsync(
                openClosuresWorkflow,
                cancellationToken);
'@

if (-not $runnerText.Contains($oldBlock)) {
    throw "No encontré el bloque V6.18.7 esperado. No se modificó WorkflowRunner.cs."
}

$runnerText = $runnerText.Replace($oldBlock, $newBlock)

$runnerText = $runnerText.Replace(
    "=== CIERRES V6.18.7 - FECHA V6.5 INTACTA + OPEN_CIERRES REPLAY GENERICO + DIAGNOSTICO COMBOBOX ===",
    "=== CIERRES V6.19 - OPEN_CIERRES POR ESTADO + FECHA V6.5 INTACTA + DIAGNOSTICO COMBOBOX ==="
)

Set-Content -Path $runner -Value $runnerText -Encoding UTF8
Write-Host "[OK] WorkflowRunner.cs actualizado."

if (Test-Path $solution) {
    Write-Host ""
    Write-Host "=== COMPILANDO ==="
    dotnet build $solution -c Release

    if ($LASTEXITCODE -ne 0) {
        throw "La compilación falló."
    }
}
else {
    Write-Warning "No se encontró la solución en $solution"
}

Write-Host ""
Write-Host "V6.19 aplicada."
Write-Host "Prueba el Trainer y copia el log desde [OPEN]."
