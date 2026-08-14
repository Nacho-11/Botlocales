using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ParrillitaIA.Trainer;

public sealed class WorkflowRecorder : IDisposable
{
    private const int HotKeyStart = 1001;
    private const int HotKeyStop = 1002;
    private const int HotKeyCheckpoint = 1003;
    private const int HotKeyYesterday = 1004;

    // Windows entrega los modificadores low-level como izquierda/derecha.
    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_RSHIFT = 0xA1;
    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_RCONTROL = 0xA3;
    private const ushort VK_LMENU = 0xA4;
    private const ushort VK_RMENU = 0xA5;

    private readonly string _local;
    private readonly string _workflow;
    private readonly List<WorkflowStep> _steps = [];
    private readonly NativeMethods.LowLevelMouseProc _mouseProc;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardProc;

    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private bool _recording;
    private bool _disposed;
    private DateTimeOffset _lastAction;
    private string _targetProcessName = string.Empty;

    public WorkflowRecorder(
        string local,
        string workflow)
    {
        _local = local;
        _workflow = workflow;
        _mouseProc = MouseCallback;
        _keyboardProc = KeyboardCallback;
    }

    public WorkflowModel Record()
    {
        RegisterHotKeys();
        InstallHooks();

        // Inicia automáticamente en modo TRAIN.
        StartRecording(clearExisting: true);

        try
        {
            while (NativeMethods.GetMessage(
                       out var message,
                       IntPtr.Zero,
                       0,
                       0) > 0)
            {
                if (message.message != NativeMethods.WM_HOTKEY)
                    continue;

                var id = (int)message.wParam.ToUInt64();

                switch (id)
                {
                    case HotKeyStart:
                        StartRecording(clearExisting: true);
                        break;

                    case HotKeyCheckpoint when _recording:
                        CaptureCheckpoint();
                        break;

                    case HotKeyYesterday when _recording:
                        CaptureYesterdayDate();
                        break;

                    case HotKeyStop when _recording:
                        _recording = false;
                        Console.Beep(1200, 150);

                        Console.WriteLine();
                        Console.WriteLine(
                            $"Grabación finalizada. Pasos capturados: {_steps.Count}");

                        NativeMethods.PostQuitMessage(0);
                        break;
                }
            }
        }
        finally
        {
            Dispose();
        }

        return new WorkflowModel
        {
            Local = _local,
            Name = _workflow,
            TargetProcessName = _targetProcessName,
            TrainedAt = DateTimeOffset.Now,
            Steps = _steps.ToList()
        };
    }

    private void StartRecording(bool clearExisting)
    {
        if (clearExisting)
        {
            _steps.Clear();
            _targetProcessName = string.Empty;
        }

        _recording = true;
        _lastAction = DateTimeOffset.Now;

        Console.Beep(900, 120);

        Console.WriteLine();
        Console.WriteLine("=== GRABANDO AUTOMÁTICAMENTE ===");
        Console.WriteLine("Ya puedes trabajar dentro de SoftRestaurant.");
        Console.WriteLine("Mouse y teclado están siendo registrados.");
        Console.WriteLine();
        Console.WriteLine("CTRL+SHIFT+F10 = checkpoint / esperar ventana");
        Console.WriteLine("CTRL+SHIFT+F11 = FECHA DE AYER");
        Console.WriteLine("CTRL+SHIFT+F9  = terminar y guardar");
        Console.WriteLine("CTRL+SHIFT+F8  = borrar y comenzar otra vez");
        Console.WriteLine();
    }

    private IntPtr MouseCallback(
        int nCode,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (nCode >= 0 &&
            _recording &&
            wParam.ToInt32() == NativeMethods.WM_LBUTTONDOWN)
        {
            try
            {
                var data =
                    Marshal.PtrToStructure<
                        NativeMethods.MSLLHOOKSTRUCT>(lParam);

                CaptureClick(
                    data.pt.X,
                    data.pt.Y);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[RECORDER] Error capturando mouse: {ex.Message}");
            }
        }

        return NativeMethods.CallNextHookEx(
            _mouseHook,
            nCode,
            wParam,
            lParam);
    }

    private IntPtr KeyboardCallback(
        int nCode,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (nCode >= 0 &&
            _recording &&
            (wParam.ToInt32() == NativeMethods.WM_KEYDOWN ||
             wParam.ToInt32() == NativeMethods.WM_SYSKEYDOWN))
        {
            try
            {
                var data =
                    Marshal.PtrToStructure<
                        NativeMethods.KBDLLHOOKSTRUCT>(lParam);

                var vk =
                    (ushort)data.vkCode;

                // Nunca grabar Ctrl/Shift/Alt, ni genéricos ni L/R.
                if (!IsModifierKey(vk) &&
                    !IsRecorderHotKey(vk))
                {
                    CaptureKey(vk);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[RECORDER] Error capturando teclado: {ex.Message}");
            }
        }

        return NativeMethods.CallNextHookEx(
            _keyboardHook,
            nCode,
            wParam,
            lParam);
    }

    private void CaptureClick(
        int x,
        int y)
    {
        var window =
            WindowInfo.GetForeground();

        if (!PrepareTarget(window))
            return;

        // No guardar clics fuera del rectángulo real de la ventana activa.
        if (x < window.Left ||
            y < window.Top ||
            x >= window.Left + window.Width ||
            y >= window.Top + window.Height)
        {
            Console.WriteLine(
                $"[RECORDER] Click ignorado fuera de ventana: " +
                $"({x},{y}) Ventana=({window.Left},{window.Top}," +
                $"{window.Width},{window.Height})");

            return;
        }

        var relativeX =
            (x - window.Left) /
            (double)window.Width;

        var relativeY =
            (y - window.Top) /
            (double)window.Height;

        // Nunca permitir 1.0: ese punto ya queda fuera del área cliente grabada.
        if (relativeX < 0.0 ||
            relativeX >= 1.0 ||
            relativeY < 0.0 ||
            relativeY >= 1.0)
        {
            Console.WriteLine(
                $"[RECORDER] Click relativo inválido ignorado: " +
                $"X={relativeX:0.0000}; Y={relativeY:0.0000}");

            return;
        }

        var now =
            DateTimeOffset.Now;

        var step =
            BaseStep(
                window,
                now) with
            {
                Order =
                    _steps.Count + 1,

                Action =
                    "LeftClick",

                ScreenX =
                    x,

                ScreenY =
                    y,

                RelativeX =
                    relativeX,

                RelativeY =
                    relativeY
            };

        Add(
            step,
            $"Click ({x},{y})");
    }

    private void CaptureKey(
        ushort virtualKey)
    {
        var window =
            WindowInfo.GetForeground();

        if (!PrepareTarget(window))
            return;

        var now =
            DateTimeOffset.Now;

        var ctrl =
            IsAnyControlDown();

        var shift =
            IsAnyShiftDown();

        var alt =
            IsAnyAltDown();

        var step =
            BaseStep(
                window,
                now) with
            {
                Order =
                    _steps.Count + 1,

                Action =
                    "KeyPress",

                VirtualKey =
                    virtualKey,

                Ctrl =
                    ctrl,

                Shift =
                    shift,

                Alt =
                    alt
            };

        Add(
            step,
            $"Key VK=0x{virtualKey:X2} " +
            $"Ctrl={ctrl} Shift={shift} Alt={alt}");
    }

    private void CaptureCheckpoint()
    {
        var window =
            WindowInfo.GetForeground();

        if (!PrepareTarget(window))
        {
            Console.WriteLine(
                "Checkpoint ignorado: SoftRestaurant no está activo.");
            return;
        }

        var step =
            BaseStep(
                window,
                DateTimeOffset.Now) with
            {
                Order =
                    _steps.Count + 1,

                Action =
                    "WaitForWindow"
            };

        Add(
            step,
            "CHECKPOINT");

        Console.Beep(
            1050,
            90);
    }

    private void CaptureYesterdayDate()
    {
        if (!_recording)
        {
            Console.WriteLine(
                "FECHA=AYER ignorada: no se está grabando.");
            return;
        }

        // Debe existir un clic anterior que haya puesto foco en Fecha.
        var previousClick =
            _steps
                .Where(
                    x => string.Equals(
                        x.Action,
                        "LeftClick",
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(
                    x => x.Order)
                .LastOrDefault();

        if (previousClick is null)
        {
            Console.WriteLine(
                "FECHA=AYER necesita un clic previo dentro del campo Fecha.");
            return;
        }

        var now =
            DateTimeOffset.Now;

        var step =
            new WorkflowStep
            {
                Order =
                    _steps.Count + 1,

                Action =
                    "SetYesterdayDate",

                DelayBeforeMs =
                    GetDelay(now),

                // Reutiliza el contexto del último clic válido.
                ProcessName =
                    previousClick.ProcessName,

                WindowTitle =
                    previousClick.WindowTitle,

                StableTitle =
                    previousClick.StableTitle,

                WindowClass =
                    previousClick.WindowClass,

                RecordedWindowWidth =
                    previousClick.RecordedWindowWidth,

                RecordedWindowHeight =
                    previousClick.RecordedWindowHeight,

                ValueFormat =
                    "dd/MM/yyyy",

                WindowWaitTimeoutMs =
                    previousClick.WindowWaitTimeoutMs
            };

        Add(
            step,
            "FECHA=AYER");

        Console.Beep(
            1150,
            100);
    }

    private WorkflowStep BaseStep(
        WindowSnapshot window,
        DateTimeOffset now)
    {
        return new WorkflowStep
        {
            DelayBeforeMs =
                GetDelay(now),

            ProcessName =
                window.ProcessName,

            WindowTitle =
                window.Title,

            StableTitle =
                TitleMatcher.BuildStableTitle(
                    window.Title),

            WindowClass =
                window.ClassName,

            RecordedWindowWidth =
                window.Width,

            RecordedWindowHeight =
                window.Height
        };
    }

    private void Add(
        WorkflowStep step,
        string description)
    {
        _steps.Add(step);
        _lastAction =
            DateTimeOffset.Now;

        Console.WriteLine(
            $"[{step.Order:000}] {description} " +
            $"Estable=\"{Display(step.StableTitle)}\" " +
            $"Clase=\"{step.WindowClass}\"");
    }

    private bool PrepareTarget(
        WindowSnapshot window)
    {
        if (!ShouldRecord(window))
            return false;

        if (string.IsNullOrWhiteSpace(
                _targetProcessName))
        {
            if (!LooksLikeSoftRestaurant(
                    window))
            {
                return false;
            }

            _targetProcessName =
                window.ProcessName;

            Console.WriteLine(
                $"Proceso objetivo detectado: {_targetProcessName}");
        }

        return string.Equals(
            window.ProcessName,
            _targetProcessName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsModifierKey(
        ushort vk) =>
        vk == NativeMethods.VK_CONTROL ||
        vk == NativeMethods.VK_SHIFT ||
        vk == NativeMethods.VK_MENU ||
        vk == VK_LSHIFT ||
        vk == VK_RSHIFT ||
        vk == VK_LCONTROL ||
        vk == VK_RCONTROL ||
        vk == VK_LMENU ||
        vk == VK_RMENU;

    private static bool IsRecorderHotKey(
        ushort vk)
    {
        if (vk != NativeMethods.VK_F8 &&
            vk != NativeMethods.VK_F9 &&
            vk != NativeMethods.VK_F10 &&
            vk != NativeMethods.VK_F11)
        {
            return false;
        }

        return IsAnyControlDown() &&
               IsAnyShiftDown();
    }

    private static bool IsAnyControlDown() =>
        IsDown(NativeMethods.VK_CONTROL) ||
        IsDown(VK_LCONTROL) ||
        IsDown(VK_RCONTROL);

    private static bool IsAnyShiftDown() =>
        IsDown(NativeMethods.VK_SHIFT) ||
        IsDown(VK_LSHIFT) ||
        IsDown(VK_RSHIFT);

    private static bool IsAnyAltDown() =>
        IsDown(NativeMethods.VK_MENU) ||
        IsDown(VK_LMENU) ||
        IsDown(VK_RMENU);

    private static bool IsDown(
        int virtualKey) =>
        (NativeMethods.GetAsyncKeyState(
             virtualKey) &
         0x8000) != 0;

    private int GetDelay(
        DateTimeOffset now) =>
        _steps.Count == 0
            ? 300
            : (int)Math.Clamp(
                (now - _lastAction)
                .TotalMilliseconds,
                100,
                30000);

    private void InstallHooks()
    {
        using var process =
            Process.GetCurrentProcess();

        using var module =
            process.MainModule
            ?? throw new InvalidOperationException(
                "No se pudo obtener el módulo actual.");

        var moduleHandle =
            NativeMethods.GetModuleHandle(
                module.ModuleName);

        _mouseHook =
            NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL,
                _mouseProc,
                moduleHandle,
                0);

        if (_mouseHook ==
            IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "No se pudo instalar el hook del mouse.");
        }

        _keyboardHook =
            NativeMethods.SetWindowsHookExKeyboard(
                NativeMethods.WH_KEYBOARD_LL,
                _keyboardProc,
                moduleHandle,
                0);

        if (_keyboardHook ==
            IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "No se pudo instalar el hook del teclado.");
        }
    }

    private static void RegisterHotKeys()
    {
        Register(
            HotKeyStart,
            NativeMethods.VK_F8,
            "CTRL+SHIFT+F8");

        Register(
            HotKeyStop,
            NativeMethods.VK_F9,
            "CTRL+SHIFT+F9");

        Register(
            HotKeyCheckpoint,
            NativeMethods.VK_F10,
            "CTRL+SHIFT+F10");

        Register(
            HotKeyYesterday,
            NativeMethods.VK_F11,
            "CTRL+SHIFT+F11");
    }

    private static void Register(
        int id,
        uint key,
        string name)
    {
        if (!NativeMethods.RegisterHotKey(
                IntPtr.Zero,
                id,
                NativeMethods.MOD_CONTROL |
                NativeMethods.MOD_SHIFT,
                key))
        {
            throw new InvalidOperationException(
                $"No se pudo registrar {name}.");
        }
    }

    private static bool LooksLikeSoftRestaurant(
        WindowSnapshot window)
    {
        if (window.Title.Contains(
                "SOFT RESTAURANT",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var process =
            NormalizeProcess(
                window.ProcessName);

        return process.Contains(
                   "softrestaurant") ||
               process.Contains(
                   "softrest") ||
               process.Contains(
                   "sr11");
    }

    private static bool ShouldRecord(
        WindowSnapshot window)
    {
        if (window.Handle ==
                IntPtr.Zero ||
            window.Width <= 0 ||
            window.Height <= 0)
        {
            return false;
        }

        var process =
            NormalizeProcess(
                window.ProcessName);

        return !process.Contains(
                   "powershell") &&
               !process.Contains(
                   "pwsh") &&
               !process.Contains(
                   "windowsterminal") &&
               !process.Contains(
                   "explorer") &&
               !process.Contains(
                   "parrillitaiatrainer");
    }

    private static string NormalizeProcess(
        string value) =>
        value
            .Replace(".", "")
            .Replace("_", "")
            .Replace("-", "")
            .Replace(" ", "")
            .ToLowerInvariant();

    private static string Display(
        string value) =>
        string.IsNullOrWhiteSpace(
            value)
            ? "(sin título)"
            : value;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed =
            true;

        NativeMethods.UnregisterHotKey(
            IntPtr.Zero,
            HotKeyStart);

        NativeMethods.UnregisterHotKey(
            IntPtr.Zero,
            HotKeyStop);

        NativeMethods.UnregisterHotKey(
            IntPtr.Zero,
            HotKeyCheckpoint);

        NativeMethods.UnregisterHotKey(
            IntPtr.Zero,
            HotKeyYesterday);

        if (_mouseHook !=
            IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(
                _mouseHook);

            _mouseHook =
                IntPtr.Zero;
        }

        if (_keyboardHook !=
            IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(
                _keyboardHook);

            _keyboardHook =
                IntPtr.Zero;
        }
    }
}
