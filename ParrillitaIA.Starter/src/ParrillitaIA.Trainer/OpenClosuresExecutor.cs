param(
    [string]$RepoRoot = "."
)

$ErrorActionPreference = "Stop"

$trainer = Join-Path $RepoRoot "ParrillitaIA.Starter\src\ParrillitaIA.Trainer"
$runner = Join-Path $trainer "WorkflowRunner.cs"
$duplicateRunner = Join-Path $trainer "WorkflowRunner..cs"
$newExecutor = Join-Path $trainer "OpenClosuresExecutor.cs"
$solution = Join-Path $RepoRoot "ParrillitaIA.Starter\ParrillitaIA.sln"

if (-not (Test-Path $runner)) {
    throw "No se encontró WorkflowRunner.cs en: $runner"
}

# 1) Quitar el duplicado que declara la misma clase WorkflowRunner.
if (Test-Path $duplicateRunner) {
    Remove-Item $duplicateRunner -Force
    Write-Host "[OK] Eliminado WorkflowRunner..cs duplicado."
}

# 2) Crear un ejecutor aislado y verificable para OPEN_CIERRES.
$executorSource = @'
using System.Text;

namespace ParrillitaIA.Trainer;

/// <summary>
/// Apertura controlada de Reportes -> Formas de pago por turno.
/// No reenfoca la ventana principal entre los dos clics y valida
/// que el estado de la UI cambie antes de continuar.
/// </summary>
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
                "OPEN_CIERRES: no se encontró la ventana principal de SOFT RESTAURANT.");
        }

        if (!NativeMethods.GetWindowRect(
                mainWindow,
                out var initialRect))
        {
            throw new InvalidOperationException(
                "OPEN_CIERRES: no se pudo leer la geometría de SOFT RESTAURANT.");
        }

        NativeMethods.SetForegroundWindow(
            mainWindow);

        await Task.Delay(
            350,
            cancellationToken);

        var firstPoint =
            ToScreenPoint(
                initialRect,
                clicks[0]);

        var secondPoint =
            ToScreenPoint(
                initialRect,
                clicks[1]);

        var beforeSecond =
            DescribePointTarget(
                secondPoint);

        Console.WriteLine();
        Console.WriteLine(
            "[OPEN] Inicio de apertura controlada.");

        Console.WriteLine(
            $"[OPEN] Ventana principal: {Describe(mainWindow)}");

        Console.WriteLine(
            $"[OPEN] Paso 1 Reportes: ({firstPoint.X},{firstPoint.Y})");

        Console.WriteLine(
            $"[OPEN] Antes del paso 1, destino del paso 2: {beforeSecond}");

        MoveAndClick(
            firstPoint);

        // No llamar SetForegroundWindow aquí.
        // Los menús legacy/transitorios pueden cerrarse al cambiar el foco.
        var menuObservation =
            await WaitForPointTargetChangeAsync(
                secondPoint,
                beforeSecond.Handle,
                3000,
                cancellationToken);

        Console.WriteLine(
            $"[OPEN] Estado tras abrir Reportes: {menuObservation.Description}");

        if (!menuObservation.Changed)
        {
            throw new InvalidOperationException(
                "OPEN_CIERRES: el clic en Reportes no produjo un cambio detectable " +
                "en la zona de 'Formas de pago por turno'. " +
                "Se detiene para no ejecutar el segundo clic a ciegas.");
        }

        Console.WriteLine(
            $"[OPEN] Paso 2 Formas de pago por turno: ({secondPoint.X},{secondPoint.Y})");

        // Importante: no reenfocar la ventana principal.
        // Interactuamos con el punto mientras el menú está abierto.
        MoveAndClick(
            secondPoint);

        var monthView =
            await SoftRestaurantReportContext
                .WaitForVisibleMonthViewAsync(
                    workflow.TargetProcessName,
                    mainWindow,
                    15000,
                    cancellationToken);

        if (monthView == IntPtr.Zero)
        {
            var afterSecond =
                DescribePointTarget(
                    secondPoint);

            throw new InvalidOperationException(
                "OPEN_CIERRES: el menú cambió correctamente después del primer clic, " +
                "pero después del segundo clic no apareció un MonthView visible. " +
                $"Destino actual del punto 2: {afterSecond}");
        }

        Console.WriteLine(
            $"[OPEN] Formas de pago por turno abierto correctamente. " +
            $"MonthView={Describe(monthView)}");
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

    private static async Task<PointObservation> WaitForPointTargetChangeAsync(
        NativeMethods.POINT point,
        IntPtr previousHandle,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var deadline =
            DateTimeOffset.UtcNow.AddMilliseconds(
                timeoutMs);

        PointTarget last =
            DescribePointTarget(
                point);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            last =
                DescribePointTarget(
                    point);

            var popupMenu =
                last.ClassName.Equals(
                    "#32768",
                    StringComparison.OrdinalIgnoreCase);

            var changed =
                last.Handle != IntPtr.Zero &&
                (last.Handle != previousHandle ||
                 popupMenu);

            if (changed)
            {
                // Dos observaciones consecutivas reducen falsos positivos
                // causados por repaint durante la apertura.
                await Task.Delay(
                    80,
                    cancellationToken);

                var confirm =
                    DescribePointTarget(
                        point);

                var confirmedPopup =
                    confirm.ClassName.Equals(
                        "#32768",
                        StringComparison.OrdinalIgnoreCase);

                if (confirm.Handle == last.Handle ||
                    confirmedPopup)
                {
                    return new PointObservation(
                        true,
                        confirm.ToString());
                }
            }

            await Task.Delay(
                75,
                cancellationToken);
        }

        return new PointObservation(
            false,
            last.ToString());
    }

    private static PointTarget DescribePointTarget(
        NativeMethods.POINT point)
    {
        var hwnd =
            NativeMethods.WindowFromPoint(
                point);

        if (hwnd == IntPtr.Zero)
        {
            return new PointTarget(
                IntPtr.Zero,
                string.Empty,
                string.Empty);
        }

        return new PointTarget(
            hwnd,
            GetClassName(hwnd),
            GetWindowText(hwnd));
    }

    private static void MoveAndClick(
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
                $"SendInput solo envió {sent}/{inputs.Length} eventos de mouse.");
        }
    }

    private static string Describe(
        IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return "HWND=0";

        return
            $"HWND=0x{hwnd.ToInt64():X} " +
            $"Class=\"{GetClassName(hwnd)}\" " +
            $"Text=\"{GetWindowText(hwnd)}\"";
    }

    private static string GetClassName(
        IntPtr hwnd)
    {
        var buffer =
            new StringBuilder(
                256);

        NativeMethods.GetClassName(
            hwnd,
            buffer,
            buffer.Capacity);

        return buffer.ToString();
    }

    private static string GetWindowText(
        IntPtr hwnd)
    {
        var buffer =
            new StringBuilder(
                512);

        NativeMethods.GetWindowText(
            hwnd,
            buffer,
            buffer.Capacity);

        return buffer.ToString().Trim();
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

    private readonly record struct PointObservation(
        bool Changed,
        string Description);
}
'@

Set-Content -Path $newExecutor -Value $executorSource -Encoding UTF8
Write-Host "[OK] Creado OpenClosuresExecutor.cs"

# 3) Cambiar UNA sola llamada en WorkflowRunner.
$runnerText = Get-Content -Path $runner -Raw

$oldCall = @'
            await ExecuteOpenClosuresWorkflowAsync(
                openClosuresWorkflow,
                cancellationToken);
'@

$newCall = @'
            await OpenClosuresExecutor.RunAsync(
                openClosuresWorkflow,
                cancellationToken);
'@

if (-not $runnerText.Contains($oldCall)) {
    throw "No encontré la llamada esperada a ExecuteOpenClosuresWorkflowAsync. El repositorio cambió; no se aplicó el reemplazo."
}

$runnerText = $runnerText.Replace($oldCall, $newCall)

# Cambiar solo el texto de diagnóstico, sin tocar lógica de fecha/usuario.
$runnerText = $runnerText.Replace(
    "=== CIERRES V6.18.6 - FECHA V6.5 INTACTA + OPEN_CIERRES SIN REFOCUS + DIAGNOSTICO COMBOBOX ===",
    "=== CIERRES V6.19 - OPEN_CIERRES POR ESTADO + FECHA INTACTA + DIAGNOSTICO COMBOBOX ==="
)

Set-Content -Path $runner -Value $runnerText -Encoding UTF8
Write-Host "[OK] WorkflowRunner conectado a OpenClosuresExecutor."

# 4) Compilar.
if (Test-Path $solution) {
    Write-Host ""
    Write-Host "=== DOTNET BUILD ==="
    dotnet build $solution -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build falló. Revisa los errores anteriores."
    }
} else {
    Write-Warning "No encontré ParrillitaIA.Starter\ParrillitaIA.sln; cambios aplicados pero no se compiló."
}

Write-Host ""
Write-Host "Cambios V6.19 aplicados correctamente."
Write-Host "Prueba recomendada:"
Write-Host '  .\ParrillitaIA.Trainer.exe test SAN_PEDRO CIERRES'
Write-Host ""
Write-Host "Envíame desde [OPEN] Inicio de apertura controlada hasta el resultado/error."
