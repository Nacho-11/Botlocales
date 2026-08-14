using System.Runtime.InteropServices;

namespace ParrillitaIA.Trainer;

public sealed class WorkflowRunner
{
    public async Task RunAsync(
        WorkflowModel workflow,
        CancellationToken cancellationToken)
    {
        if (workflow.Steps.Count == 0)
        {
            throw new InvalidOperationException(
                "El flujo no contiene pasos.");
        }

        foreach (var step in workflow.Steps
                     .OrderBy(x => x.Order))
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            await Task.Delay(
                Math.Clamp(
                    step.DelayBeforeMs,
                    100,
                    30000),
                cancellationToken);

            Console.WriteLine(
                $"[{step.Order:000}] {step.Action} " +
                $"Proceso=\"{step.ProcessName}\" " +
                $"Estable=\"{Display(step.StableTitle)}\"");

            // SetYesterdayDate es especial:
            // el clic anterior ya dejó foco en el campo Fecha.
            // NO hacemos SetForegroundWindow aquí porque podría robar ese foco.
            if (step.Action.Equals(
                    "SetYesterdayDate",
                    StringComparison.OrdinalIgnoreCase))
            {
                await PasteYesterdayAsync(
                    step.ValueFormat,
                    cancellationToken);

                continue;
            }

            // Acciones que dependen del foco dejado por el paso anterior.
            // NO debemos enfocar nuevamente la ventana.
            if (step.Action.Equals(
                    "KeyPress",
                    StringComparison.OrdinalIgnoreCase))
            {
                await SendKeyToLastControlAsync(
                    step.VirtualKey,
                    step.Ctrl,
                    step.Shift,
                    step.Alt,
                    cancellationToken);

                continue;
            }

            if (step.Action.Equals(
                    "SetYesterdayDate",
                    StringComparison.OrdinalIgnoreCase))
            {
                await PasteYesterdayAsync(
                    step.ValueFormat,
                    cancellationToken);

                continue;
            }

            var handle =
                await WaitForWindowAsync(
                    step,
                    cancellationToken);

            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"No apareció la ventana esperada en paso {step.Order}.");
            }

            NativeMethods.SetForegroundWindow(
                handle);

            await Task.Delay(
                200,
                cancellationToken);

            switch (step.Action)
            {
                case "WaitForWindow":
                    Console.WriteLine(
                        "      ✓ Checkpoint encontrado.");
                    break;

                case "LeftClick":
                    await ExecuteClickAsync(
                        step,
                        handle,
                        cancellationToken);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Acción desconocida: {step.Action}");
            }
        }
    }

    private static IntPtr _lastInputTarget = IntPtr.Zero;

    private static async Task ExecuteClickAsync(
        WorkflowStep step,
        IntPtr handle,
        CancellationToken cancellationToken)
    {
        if (!NativeMethods.GetWindowRect(
                handle,
                out var rect))
        {
            throw new InvalidOperationException(
                $"No se pudo leer la ventana en paso {step.Order}.");
        }

        // Protección extra para flujos viejos o corruptos.
        if (step.RelativeX < 0.0 ||
            step.RelativeX >= 1.0 ||
            step.RelativeY < 0.0 ||
            step.RelativeY >= 1.0)
        {
            throw new InvalidOperationException(
                $"Paso {step.Order}: coordenada relativa inválida " +
                $"X={step.RelativeX:0.0000}; Y={step.RelativeY:0.0000}. " +
                "Reentrena este flujo.");
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

        // Asegurar que el punto calculado esté realmente dentro de la ventana.
        if (x < rect.Left ||
            x >= rect.Right ||
            y < rect.Top ||
            y >= rect.Bottom)
        {
            throw new InvalidOperationException(
                $"Paso {step.Order}: clic calculado fuera de ventana en ({x},{y}).");
        }

        if (!await TryMoveCursorAsync(
                x,
                y,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"No se pudo mover el cursor a ({x},{y}) en paso {step.Order}.");
        }

        await Task.Delay(
            100,
            cancellationToken);

        Click();

        await Task.Delay(
            150,
            cancellationToken);

        var point =
            new NativeMethods.POINT
            {
                X = x,
                Y = y
            };

        _lastInputTarget =
            NativeMethods.WindowFromPoint(
                point);

        Console.WriteLine(
            $"      Control destino HWND=0x{_lastInputTarget.ToInt64():X}");
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

        while (DateTimeOffset.UtcNow <
               deadline)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var handle =
                WindowInfo.FindBestWindow(
                    step);

            if (handle !=
                IntPtr.Zero)
            {
                return handle;
            }

            await Task.Delay(
                250,
                cancellationToken);
        }

        return IntPtr.Zero;
    }

    private static async Task<bool> TryMoveCursorAsync(
        int x,
        int y,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1;
             attempt <= 5;
             attempt++)
        {
            if (NativeMethods.SetCursorPos(
                    x,
                    y))
            {
                return true;
            }

            await Task.Delay(
                250 * attempt,
                cancellationToken);
        }

        return false;
    }

    private static void SendKey(
        ushort virtualKey,
        bool ctrl,
        bool shift,
        bool alt)
    {
        var inputs =
            new List<NativeMethods.INPUT>();

        if (ctrl)
        {
            inputs.Add(
                KeyDown(
                    NativeMethods.VK_CONTROL));
        }

        if (shift)
        {
            inputs.Add(
                KeyDown(
                    NativeMethods.VK_SHIFT));
        }

        if (alt)
        {
            inputs.Add(
                KeyDown(
                    NativeMethods.VK_MENU));
        }

        inputs.Add(
            KeyDown(
                virtualKey));

        inputs.Add(
            KeyUp(
                virtualKey));

        if (alt)
        {
            inputs.Add(
                KeyUp(
                    NativeMethods.VK_MENU));
        }

        if (shift)
        {
            inputs.Add(
                KeyUp(
                    NativeMethods.VK_SHIFT));
        }

        if (ctrl)
        {
            inputs.Add(
                KeyUp(
                    NativeMethods.VK_CONTROL));
        }

        Send(
            inputs.ToArray());
    }

    private static async Task PasteYesterdayAsync(
        string format,
        CancellationToken cancellationToken)
    {
        var value =
            DateTime.Today
                .AddDays(-1)
                .ToString(
                    string.IsNullOrWhiteSpace(
                        format)
                        ? "dd/MM/yyyy"
                        : format);

        Console.WriteLine(
            $"      Fecha calculada: {value}");

        ClipboardHelper.SetText(
            value);

        // El campo Fecha debe seguir con foco por el clic anterior.
        SendKey(
            NativeMethods.VK_A,
            ctrl: true,
            shift: false,
            alt: false);

        await Task.Delay(
            120,
            cancellationToken);

        SendKey(
            NativeMethods.VK_V,
            ctrl: true,
            shift: false,
            alt: false);

        await Task.Delay(
            250,
            cancellationToken);

        ClipboardHelper.TryClear();
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
                                key,

                            dwFlags =
                                IsExtendedKey(key)
                                    ? NativeMethods.KEYEVENTF_EXTENDEDKEY
                                    : 0
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
                                NativeMethods.KEYEVENTF_KEYUP |
                                (IsExtendedKey(key)
                                    ? NativeMethods.KEYEVENTF_EXTENDEDKEY
                                    : 0)
                        }
                }
        };

    private static void Click()
    {
        Send(new[]
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
                                    NativeMethods
                                        .MOUSEEVENTF_LEFTDOWN
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
                                    NativeMethods
                                        .MOUSEEVENTF_LEFTUP
                            }
                    }
            }
        });
    }

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
                $"Windows envió {sent}/{inputs.Length} eventos.");
        }
    }

    private static string Display(
        string value) =>
        string.IsNullOrWhiteSpace(
            value)
            ? "(sin título)"
            : value;

    private static bool IsExtendedKey(
        ushort key)
    {
        return key switch
        {
            0x21 => true, // Page Up
            0x22 => true, // Page Down
            0x23 => true, // End
            0x24 => true, // Home

            0x25 => true, // Left
            0x26 => true, // Up
            0x27 => true, // Right
            0x28 => true, // Down

            0x2D => true, // Insert
            0x2E => true, // Delete

            _ => false
        };
    }

    private static async Task SendKeyToLastControlAsync(
        ushort virtualKey,
        bool ctrl,
        bool shift,
        bool alt,
        CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $"      Enviando VK=0x{virtualKey:X2} " +
            $"a HWND=0x{_lastInputTarget.ToInt64():X}");

        if (_lastInputTarget != IntPtr.Zero &&
            !ctrl &&
            !shift &&
            !alt)
        {
            NativeMethods.SendMessage(
                _lastInputTarget,
                NativeMethods.WM_KEYDOWN,
                new IntPtr(virtualKey),
                IntPtr.Zero);

            await Task.Delay(
                100,
                cancellationToken);

            NativeMethods.SendMessage(
                _lastInputTarget,
                NativeMethods.WM_KEYUP,
                new IntPtr(virtualKey),
                IntPtr.Zero);

            await Task.Delay(
                120,
                cancellationToken);

            return;
        }

        // Fallback para texto y combinaciones Ctrl/Shift/Alt.
        SendKey(
            virtualKey,
            ctrl,
            shift,
            alt);

        await Task.Delay(
            120,
            cancellationToken);
    }        
}
