using System.Text.Json;
using System.Text.Json.Nodes;

namespace ParrillitaIA.Trainer;

public sealed class LoginCalibrator : IDisposable
{
    private const int HotKeyUsername = 2101;
    private const int HotKeyPassword = 2102;
    private const int HotKeyButton = 2103;
    private const int HotKeySave = 2104;

    private readonly SoftRestaurantLauncher _launcher;
    private readonly string _settingsPath;

    private double? _usernameX;
    private double? _usernameY;
    private double? _passwordX;
    private double? _passwordY;
    private double? _buttonX;
    private double? _buttonY;
    private bool _disposed;

    public LoginCalibrator(
        SoftRestaurantLauncher launcher,
        string settingsPath)
    {
        _launcher = launcher;
        _settingsPath = settingsPath;
    }

    public void Run()
    {
        var loginWindow = _launcher.FindLoginWindow();

        if (loginWindow == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "No se encontró la ventana 'Inicio de sesión'.");
        }

        NativeMethods.SetForegroundWindow(loginWindow);

        Register(
            HotKeyUsername,
            NativeMethods.VK_1,
            "CTRL+SHIFT+1");

        Register(
            HotKeyPassword,
            NativeMethods.VK_2,
            "CTRL+SHIFT+2");

        Register(
            HotKeyButton,
            NativeMethods.VK_3,
            "CTRL+SHIFT+3");

        Register(
            HotKeySave,
            NativeMethods.VK_F9,
            "CTRL+SHIFT+F9");

        Console.WriteLine();
        Console.WriteLine("=== CALIBRACIÓN LOGIN SOFTRESTAURANT ===");
        Console.WriteLine();
        Console.WriteLine("1. Pon el mouse en el centro del campo USUARIO");
        Console.WriteLine("   y pulsa CTRL+SHIFT+1");
        Console.WriteLine();
        Console.WriteLine("2. Pon el mouse en el centro del campo CONTRASEÑA");
        Console.WriteLine("   y pulsa CTRL+SHIFT+2");
        Console.WriteLine();
        Console.WriteLine("3. Pon el mouse en el centro del botón INICIAR");
        Console.WriteLine("   y pulsa CTRL+SHIFT+3");
        Console.WriteLine();
        Console.WriteLine("4. Cuando tengas los tres puntos:");
        Console.WriteLine("   CTRL+SHIFT+F9 para guardar");
        Console.WriteLine();

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

                if (id == HotKeyUsername)
                {
                    Capture(
                        loginWindow,
                        "USUARIO",
                        out _usernameX,
                        out _usernameY);
                }
                else if (id == HotKeyPassword)
                {
                    Capture(
                        loginWindow,
                        "CONTRASEÑA",
                        out _passwordX,
                        out _passwordY);
                }
                else if (id == HotKeyButton)
                {
                    Capture(
                        loginWindow,
                        "INICIAR",
                        out _buttonX,
                        out _buttonY);
                }
                else if (id == HotKeySave)
                {
                    if (!HasAllPoints())
                    {
                        Console.WriteLine(
                            "Faltan puntos. Debes capturar Usuario, Contraseña e Iniciar.");
                        continue;
                    }

                    Save();
                    NativeMethods.PostQuitMessage(0);
                }
            }
        }
        finally
        {
            Dispose();
        }
    }

    private static void Capture(
        IntPtr loginWindow,
        string name,
        out double? relativeX,
        out double? relativeY)
    {
        relativeX = null;
        relativeY = null;

        if (!NativeMethods.GetWindowRect(
                loginWindow,
                out var rect) ||
            rect.Width <= 0 ||
            rect.Height <= 0)
        {
            Console.WriteLine(
                $"No se pudo leer la ventana para {name}.");
            return;
        }

        if (!NativeMethods.GetCursorPos(
                out var cursor))
        {
            Console.WriteLine(
                $"No se pudo leer el cursor para {name}.");
            return;
        }

        var rx =
            (cursor.X - rect.Left) /
            (double)rect.Width;

        var ry =
            (cursor.Y - rect.Top) /
            (double)rect.Height;

        if (rx < 0 || rx > 1 ||
            ry < 0 || ry > 1)
        {
            Console.WriteLine(
                $"{name}: el cursor no está dentro de la ventana de login.");
            return;
        }

        relativeX = rx;
        relativeY = ry;

        Console.Beep(1000, 80);

        Console.WriteLine(
            $"{name} capturado: X={rx:F4}, Y={ry:F4}");
    }

    private bool HasAllPoints() =>
        _usernameX.HasValue &&
        _usernameY.HasValue &&
        _passwordX.HasValue &&
        _passwordY.HasValue &&
        _buttonX.HasValue &&
        _buttonY.HasValue;

    private void Save()
    {
        if (!File.Exists(_settingsPath))
        {
            throw new FileNotFoundException(
                $"No se encontró {_settingsPath}");
        }

        var root =
            JsonNode.Parse(
                File.ReadAllText(_settingsPath))
            ?? throw new InvalidOperationException(
                "trainer.settings.json no es válido.");

        var softRestaurant =
            root["SoftRestaurant"] as JsonObject
            ?? throw new InvalidOperationException(
                "Falta SoftRestaurant en trainer.settings.json.");

        softRestaurant["LoginUsernameX"] = _usernameX!.Value;
        softRestaurant["LoginUsernameY"] = _usernameY!.Value;

        softRestaurant["LoginPasswordX"] = _passwordX!.Value;
        softRestaurant["LoginPasswordY"] = _passwordY!.Value;

        softRestaurant["LoginButtonX"] = _buttonX!.Value;
        softRestaurant["LoginButtonY"] = _buttonY!.Value;

        File.WriteAllText(
            _settingsPath,
            root.ToJsonString(
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        Console.WriteLine();
        Console.WriteLine(
            $"Calibración guardada en: {_settingsPath}");

        Console.WriteLine(
            $"Usuario:     X={_usernameX:F4}, Y={_usernameY:F4}");

        Console.WriteLine(
            $"Contraseña:  X={_passwordX:F4}, Y={_passwordY:F4}");

        Console.WriteLine(
            $"Iniciar:     X={_buttonX:F4}, Y={_buttonY:F4}");
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        NativeMethods.UnregisterHotKey(
            IntPtr.Zero,
            HotKeyUsername);

        NativeMethods.UnregisterHotKey(
            IntPtr.Zero,
            HotKeyPassword);

        NativeMethods.UnregisterHotKey(
            IntPtr.Zero,
            HotKeyButton);

        NativeMethods.UnregisterHotKey(
            IntPtr.Zero,
            HotKeySave);
    }
}
