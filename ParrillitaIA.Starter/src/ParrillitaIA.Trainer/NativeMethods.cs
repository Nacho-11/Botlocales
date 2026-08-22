using System.Runtime.InteropServices;
using System.Text;

namespace ParrillitaIA.Trainer;

internal static class NativeMethods
{
    internal const int WH_KEYBOARD_LL = 13;
    internal const int WH_MOUSE_LL = 14;

    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_LBUTTONDOWN = 0x0201;
    internal const int WM_HOTKEY = 0x0312;

    internal const uint WM_SETTEXT = 0x000C;
    internal const uint WM_GETTEXTLENGTH = 0x000E;
    internal const uint WM_SETFOCUS = 0x0007;
    internal const uint WM_CLOSE = 0x0010;

    // ComboBox (Win32/VB6 compatible)
    internal const uint CB_GETCOUNT = 0x0146;
    internal const uint CB_GETCURSEL = 0x0147;
    internal const uint CB_GETLBTEXT = 0x0148;
    internal const uint CB_GETLBTEXTLEN = 0x0149;
    internal const uint CB_SETCURSEL = 0x014E;
    internal const int CB_ERR = -1;

    internal const uint WM_COMMAND = 0x0111;
    internal const int CBN_SELCHANGE = 1;

    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;

    internal const uint VK_F8 = 0x77;
    internal const uint VK_F9 = 0x78;
    internal const uint VK_F10 = 0x79;
    internal const uint VK_F11 = 0x7A;

    internal const uint VK_1 = 0x31;
    internal const uint VK_2 = 0x32;
    internal const uint VK_3 = 0x33;

    internal const ushort VK_SHIFT = 0x10;
    internal const ushort VK_CONTROL = 0x11;
    internal const ushort VK_MENU = 0x12;
    internal const ushort VK_V = 0x56;
    internal const ushort VK_A = 0x41;

    internal const uint INPUT_MOUSE = 0;
    internal const uint INPUT_KEYBOARD = 1;

    internal const uint MOUSEEVENTF_MOVE = 0x0001;
    internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    internal const uint MOUSEEVENTF_LEFTUP = 0x0004;
    internal const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    internal const uint MOUSEEVENTF_WHEEL = 0x0800;

    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    internal const uint WM_NULL = 0x0000;
    internal const uint SMTO_ABORTIFHUNG = 0x0002;

    // Clipboard
    internal const uint CF_UNICODETEXT = 13;
    internal const uint GMEM_MOVEABLE = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;

        public bool Contains(POINT p) =>
            p.X >= Left && p.X < Right &&
            p.Y >= Top && p.Y < Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public INPUTUNION Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    internal delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowsHookExW")]
    internal static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelMouseProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowsHookExW")]
    internal static extern IntPtr SetWindowsHookExKeyboard(
        int idHook,
        LowLevelKeyboardProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(
        IntPtr hhk,
        int nCode,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, string? lParam);

    [DllImport("user32.dll")]
    internal static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessage(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        StringBuilder lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(
        IntPtr hWnd,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    internal static extern int GetMessage(out MSG message, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT point);

    // Clipboard
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    internal const uint WM_KEYUP = 0x0101;

    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(
        POINT point);

    [DllImport("user32.dll")]
    internal static extern IntPtr ChildWindowFromPoint(
        IntPtr hWndParent,
        POINT point);
        
    [DllImport("user32.dll")]
    internal static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern int GetDlgCtrlID(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    internal static extern IntPtr GetDC(
        IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(
        IntPtr hWnd,
        IntPtr hDC);

    [DllImport("gdi32.dll")]
    internal static extern uint GetPixel(
        IntPtr hdc,
        int nXPos,
        int nYPos); 

    // ===== V371 CIERRES DINÁMICOS =====
    internal const uint BM_CLICK = 0x00F5;


    [DllImport("user32.dll")]
    internal static extern IntPtr SetFocus(IntPtr hWnd);

}