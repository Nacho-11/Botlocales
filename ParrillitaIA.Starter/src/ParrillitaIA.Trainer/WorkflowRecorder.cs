using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ParrillitaIA.Trainer;

public sealed class WorkflowRecorder : IDisposable
{
    private const int HotKeyStart = 1001;
    private const int HotKeyStop = 1002;

    private readonly string _local;
    private readonly string _workflow;

    private readonly List<WorkflowStep> _steps = [];
    private readonly NativeMethods.LowLevelMouseProc _mouseProc;

    private IntPtr _mouseHook = IntPtr.Zero;
    private bool _recording;
    private DateTimeOffset _lastClick;
    private bool _disposed;

    public WorkflowRecorder(string local, string workflow)
    {
        _local = local;
        _workflow = workflow;
        _mouseProc = MouseHookCallback;
    }

    public WorkflowModel Record()
    {
        RegisterHotKeys();
        InstallMouseHook();

        Console.WriteLine("Esperando CTRL + SHIFT + F8...");

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

                var id = unchecked((int)message.wParam.ToUInt64());

                if (id == HotKeyStart)
                {
                    _steps.Clear();
                    _recording = true;
                    _lastClick = DateTimeOffset.Now;

                    Console.Beep(900, 120);
                    Console.WriteLine();
                    Console.WriteLine("GRABANDO. Realiza el proceso manual.");
                    Console.WriteLine("CTRL + SHIFT + F9 para finalizar.");
                }
                else if (id == HotKeyStop && _recording)
                {
                    _recording = false;
                    Console.Beep(1200, 150);
                    NativeMethods.PostQuitMessage(0);
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
            TrainedAt = DateTimeOffset.Now,
            RecordedScreenWidth = NativeMethods.GetSystemMetrics(0),
            RecordedScreenHeight = NativeMethods.GetSystemMetrics(1),
            Steps = _steps.ToList()
        };
    }

    private IntPtr MouseHookCallback(
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
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                CaptureClick(data.pt.X, data.pt.Y);
            }
            catch
            {
                // Nunca romper la cadena global de hooks por un fallo de grabación.
            }
        }

        return NativeMethods.CallNextHookEx(
            _mouseHook,
            nCode,
            wParam,
            lParam);
    }

    private void CaptureClick(int x, int y)
    {
        var now = DateTimeOffset.Now;
        var window = WindowInfo.GetForeground();

        if (window.Handle == IntPtr.Zero ||
            window.Width <= 0 ||
            window.Height <= 0)
        {
            return;
        }

        var relativeX = Math.Clamp(
            (x - window.Left) / (double)window.Width,
            0.0,
            1.0);

        var relativeY = Math.Clamp(
            (y - window.Top) / (double)window.Height,
            0.0,
            1.0);

        var delay = _steps.Count == 0
            ? 500
            : (int)Math.Clamp(
                (now - _lastClick).TotalMilliseconds,
                100,
                30_000);

        _lastClick = now;

        var step = new WorkflowStep
        {
            Order = _steps.Count + 1,
            DelayBeforeMs = delay,
            Action = "LeftClick",
            WindowTitle = window.Title,
            WindowClass = window.ClassName,
            ScreenX = x,
            ScreenY = y,
            RelativeX = relativeX,
            RelativeY = relativeY,
            RecordedWindowLeft = window.Left,
            RecordedWindowTop = window.Top,
            RecordedWindowWidth = window.Width,
            RecordedWindowHeight = window.Height
        };

        _steps.Add(step);

        Console.WriteLine(
            $"[{step.Order:000}] Click ({x},{y}) " +
            $"Ventana=\"{Shorten(window.Title, 70)}\"");
    }

    private void InstallMouseHook()
    {
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule
            ?? throw new InvalidOperationException(
                "No se pudo obtener el módulo actual.");

        var moduleHandle =
            NativeMethods.GetModuleHandle(module.ModuleName);

        _mouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _mouseProc,
            moduleHandle,
            0);

        if (_mouseHook == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"No se pudo instalar el hook del mouse. Win32={Marshal.GetLastWin32Error()}");
        }
    }

    private static string Shorten(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(sin título)";

        return value.Length <= max
            ? value
            : value[..max] + "...";
    }

    private static void RegisterHotKeys()
    {
        if (!NativeMethods.RegisterHotKey(
                IntPtr.Zero,
                HotKeyStart,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                NativeMethods.VK_F8))
        {
            throw new InvalidOperationException(
                "No se pudo registrar CTRL+SHIFT+F8.");
        }

        if (!NativeMethods.RegisterHotKey(
                IntPtr.Zero,
                HotKeyStop,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                NativeMethods.VK_F9))
        {
            NativeMethods.UnregisterHotKey(IntPtr.Zero, HotKeyStart);

            throw new InvalidOperationException(
                "No se pudo registrar CTRL+SHIFT+F9.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        NativeMethods.UnregisterHotKey(IntPtr.Zero, HotKeyStart);
        NativeMethods.UnregisterHotKey(IntPtr.Zero, HotKeyStop);

        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }
}
