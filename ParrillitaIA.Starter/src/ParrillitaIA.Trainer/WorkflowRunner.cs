using System.Runtime.InteropServices;

namespace ParrillitaIA.Trainer;

public sealed class WorkflowRunner
{
    public async Task RunAsync(
        WorkflowModel workflow,
        CancellationToken cancellationToken)
    {
        if (workflow.Steps.Count == 0)
            throw new InvalidOperationException(
                "El flujo no contiene pasos.");

        foreach (var step in workflow.Steps.OrderBy(x => x.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(
                TimeSpan.FromMilliseconds(
                    Math.Clamp(step.DelayBeforeMs, 100, 30_000)),
                cancellationToken);

            var targetWindow = WindowInfo.FindBestWindow(
                step.WindowTitle,
                step.WindowClass);

            int x;
            int y;

            if (targetWindow != IntPtr.Zero &&
                NativeMethods.GetWindowRect(
                    targetWindow,
                    out var rect) &&
                rect.Width > 0 &&
                rect.Height > 0)
            {
                NativeMethods.SetForegroundWindow(targetWindow);

                // Dar tiempo a Windows para activar la ventana.
                await Task.Delay(150, cancellationToken);

                x = rect.Left + (int)Math.Round(
                    rect.Width * step.RelativeX);

                y = rect.Top + (int)Math.Round(
                    rect.Height * step.RelativeY);
            }
            else
            {
                // Respaldo: coordenada absoluta grabada.
                x = step.ScreenX;
                y = step.ScreenY;
            }

            Console.WriteLine(
                $"[{step.Order:000}] {step.Action} -> ({x},{y}) " +
                $"Ventana=\"{step.WindowTitle}\"");

            Click(x, y);
        }
    }

    private static void Click(int x, int y)
    {
        if (!NativeMethods.SetCursorPos(x, y))
        {
            throw new InvalidOperationException(
                $"No se pudo mover el cursor a ({x},{y}).");
        }

        var inputs = new[]
        {
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                mi = new NativeMethods.MOUSEINPUT
                {
                    dwFlags = NativeMethods.MOUSEEVENTF_LEFTDOWN
                }
            },
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                mi = new NativeMethods.MOUSEINPUT
                {
                    dwFlags = NativeMethods.MOUSEEVENTF_LEFTUP
                }
            }
        };

        var sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.INPUT>());

        if (sent != inputs.Length)
        {
            throw new InvalidOperationException(
                $"SendInput solo envió {sent} de {inputs.Length} eventos.");
        }
    }
}
