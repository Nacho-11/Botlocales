using System.Runtime.InteropServices;
using System.Text;

namespace ParrillitaIA.Trainer;

public sealed class SoftRestaurantLogin
{
    private const int MaxLoginAttempts = 3;

    private readonly SoftRestaurantSettings _settings;
    private readonly string _credentialTarget;

    public SoftRestaurantLogin(
        SoftRestaurantSettings settings,
        string credentialTarget)
    {
        _settings = settings;
        _credentialTarget = credentialTarget;
    }

    public async Task LoginIfNeededAsync(
        SoftRestaurantLauncher launcher,
        CancellationToken cancellationToken)
    {
        var loginWindow =
            launcher.FindLoginWindow();

        if (loginWindow == IntPtr.Zero)
        {
            Console.WriteLine(
                "No se requiere inicio de sesión.");

            return;
        }

        Console.WriteLine();
        Console.WriteLine(
            "SoftRestaurant requiere inicio de sesión.");

        var username =
            _settings.Username;

        string password;

        if (CredentialStore.TryRead(
                _credentialTarget,
                out var savedUsername,
                out var savedPassword))
        {
            if (string.IsNullOrWhiteSpace(
                    username))
            {
                username =
                    savedUsername;
            }

            password =
                savedPassword;

            Console.WriteLine(
                $"[LOGIN] Credencial protegida encontrada para {_credentialTarget}.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(
                    username))
            {
                Console.Write(
                    "Usuario: ");

                username =
                    Console.ReadLine() ??
                    string.Empty;
            }

            if (string.IsNullOrWhiteSpace(
                    username))
            {
                throw new InvalidOperationException(
                    "El usuario no puede estar vacío.");
            }

            Console.Write(
                "Contraseña: ");

            password =
                ReadPassword();

            Console.WriteLine();
        }

        if (string.IsNullOrWhiteSpace(
                username) ||
            string.IsNullOrEmpty(
                password))
        {
            throw new InvalidOperationException(
                "No hay credenciales válidas para iniciar sesión.");
        }

        try
        {
            Console.WriteLine(
                $"[LOGIN-V2] Inicio robusto habilitado. Intentos={MaxLoginAttempts}.");

            for (var attempt = 1;
                 attempt <= MaxLoginAttempts;
                 attempt++)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                var mainWindow =
                    launcher.FindMainWindow();

                if (mainWindow !=
                    IntPtr.Zero)
                {
                    Console.WriteLine(
                        $"[LOGIN-V2][OK] La ventana principal ya está disponible antes del intento {attempt}.");

                    NativeMethods.SetForegroundWindow(
                        mainWindow);

                    return;
                }

                loginWindow =
                    launcher.FindLoginWindow();

                if (loginWindow ==
                    IntPtr.Zero)
                {
                    Console.WriteLine(
                        $"[LOGIN-V2][WAIT] Intento={attempt}/{MaxLoginAttempts}; " +
                        "login no visible. Esperando transición...");

                    if (await WaitForMainWindowAsync(
                            launcher,
                            TimeSpan.FromSeconds(8),
                            cancellationToken))
                    {
                        return;
                    }

                    DumpVisibleSoftRestaurantWindows(
                        "LOGIN_NO_VISIBLE");

                    continue;
                }

                if (!NativeMethods.GetWindowRect(
                        loginWindow,
                        out var rect) ||
                    rect.Width <= 0 ||
                    rect.Height <= 0)
                {
                    Console.WriteLine(
                        $"[LOGIN-V2][WARN] Intento={attempt}/{MaxLoginAttempts}; " +
                        "no se pudo obtener un rectángulo válido del login.");

                    DumpVisibleSoftRestaurantWindows(
                        "LOGIN_RECT_INVALIDO");

                    await Task.Delay(
                        1000,
                        cancellationToken);

                    continue;
                }

                Console.WriteLine();
                Console.WriteLine(
                    $"[LOGIN-V2][TRY] Intento={attempt}/{MaxLoginAttempts}; " +
                    $"HWND=0x{loginWindow.ToInt64():X}; " +
                    $"Rect=({rect.Left},{rect.Top},{rect.Width},{rect.Height})");

                await RecoverLoginWindowAsync(
                    loginWindow,
                    cancellationToken);

                Console.WriteLine(
                    "[LOGIN] Click Usuario...");

                await ClickRelativeAsync(
                    rect,
                    _settings.LoginUsernameX,
                    _settings.LoginUsernameY,
                    cancellationToken);

                await Task.Delay(
                    250,
                    cancellationToken);

                Console.WriteLine(
                    "[LOGIN] Pegando usuario...");

                ClipboardHelper.SetText(
                    username);

                await PasteAsync(
                    cancellationToken);

                await Task.Delay(
                    500,
                    cancellationToken);

                loginWindow =
                    launcher.FindLoginWindow();

                if (loginWindow !=
                    IntPtr.Zero)
                {
                    NativeMethods.SetForegroundWindow(
                        loginWindow);

                    await Task.Delay(
                        250,
                        cancellationToken);
                }

                Console.WriteLine(
                    "[LOGIN] Click Contraseña...");

                await ClickRelativeAsync(
                    rect,
                    _settings.LoginPasswordX,
                    _settings.LoginPasswordY,
                    cancellationToken);

                await Task.Delay(
                    250,
                    cancellationToken);

                Console.WriteLine(
                    "[LOGIN] Pegando contraseña...");

                ClipboardHelper.SetText(
                    password);

                await PasteAsync(
                    cancellationToken);

                await Task.Delay(
                    600,
                    cancellationToken);

                ClipboardHelper.TryClear();

                loginWindow =
                    launcher.FindLoginWindow();

                if (loginWindow !=
                    IntPtr.Zero)
                {
                    NativeMethods.SetForegroundWindow(
                        loginWindow);

                    await Task.Delay(
                        300,
                        cancellationToken);
                }

                Console.WriteLine(
                    "[LOGIN] Click INICIAR...");

                await ClickRelativeAsync(
                    rect,
                    _settings.LoginButtonX,
                    _settings.LoginButtonY,
                    cancellationToken);

                Console.WriteLine(
                    $"[LOGIN-V2][WAIT] Intento={attempt}/{MaxLoginAttempts}; " +
                    "esperando resultado del login...");

                if (await WaitForMainWindowAsync(
                        launcher,
                        TimeSpan.FromSeconds(10),
                        cancellationToken))
                {
                    return;
                }

                loginWindow =
                    launcher.FindLoginWindow();

                if (loginWindow !=
                    IntPtr.Zero)
                {
                    Console.WriteLine(
                        $"[LOGIN-V2][RETRY] Intento={attempt}/{MaxLoginAttempts}; " +
                        "el formulario de login sigue visible.");

                    DumpVisibleSoftRestaurantWindows(
                        $"DESPUES_INTENTO_{attempt}");

                    await RecoverLoginWindowAsync(
                        loginWindow,
                        cancellationToken);

                    await Task.Delay(
                        1200,
                        cancellationToken);
                }
                else
                {
                    Console.WriteLine(
                        $"[LOGIN-V2][RETRY] Intento={attempt}/{MaxLoginAttempts}; " +
                        "el login desapareció pero aún no aparece la ventana principal.");

                    DumpVisibleSoftRestaurantWindows(
                        $"TRANSICION_INTENTO_{attempt}");

                    await Task.Delay(
                        1500,
                        cancellationToken);
                }
            }

            DumpVisibleSoftRestaurantWindows(
                "FALLO_FINAL");

            throw new TimeoutException(
                $"El formulario de inicio de sesión no avanzó a la ventana principal " +
                $"después de {MaxLoginAttempts} intentos internos.");
        }
        finally
        {
            ClipboardHelper.TryClear();
            password =
                string.Empty;
        }
    }

    private static async Task RecoverLoginWindowAsync(
        IntPtr loginWindow,
        CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $"[LOGIN-V2][FOCUS] Recuperando login HWND=0x{loginWindow.ToInt64():X}...");

        NativeMethods.SetForegroundWindow(
            loginWindow);

        await Task.Delay(
            350,
            cancellationToken);

        if (NativeMethods.GetWindowRect(
                loginWindow,
                out var rect) &&
            rect.Width > 20 &&
            rect.Height > 20)
        {
            var neutralX =
                rect.Left +
                Math.Min(
                    Math.Max(
                        10,
                        rect.Width / 2),
                    rect.Width - 10);

            var neutralY =
                rect.Top +
                Math.Min(
                    18,
                    rect.Height - 10);

            MoveMouseAbsolute(
                neutralX,
                neutralY);

            await Task.Delay(
                120,
                cancellationToken);

            ClickMouse();

            await Task.Delay(
                250,
                cancellationToken);

            NativeMethods.SetForegroundWindow(
                loginWindow);
        }

        await Task.Delay(
            250,
            cancellationToken);
    }

    private static async Task<bool> WaitForMainWindowAsync(
        SoftRestaurantLauncher launcher,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline =
            DateTimeOffset.UtcNow +
            timeout;

        while (DateTimeOffset.UtcNow <
               deadline)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var main =
                launcher.FindMainWindow();

            if (main !=
                IntPtr.Zero)
            {
                Console.WriteLine(
                    "Inicio de sesión completado.");

                Console.WriteLine(
                    "[LOGIN-V2][OK] Ventana principal detectada.");

                NativeMethods.SetForegroundWindow(
                    main);

                return true;
            }

            await Task.Delay(
                300,
                cancellationToken);
        }

        return false;
    }

    private static void DumpVisibleSoftRestaurantWindows(
        string reason)
    {
        try
        {
            var windows =
                WindowInfo.GetVisibleWindowsForProcess(
                    "softrestaurant");

            Console.WriteLine(
                $"[LOGIN-V2][DIAG] Motivo={reason}; VentanasVisibles={windows.Count}");

            foreach (var window in windows)
            {
                Console.WriteLine(
                    $"[LOGIN-V2][WINDOW] " +
                    $"HWND=0x{window.Handle.ToInt64():X}; " +
                    $"Class=\"{window.ClassName}\"; " +
                    $"Title=\"{window.Title}\"; " +
                    $"Rect=({window.Left},{window.Top},{window.Width},{window.Height})");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[LOGIN-V2][DIAG][WARN] {ex.Message}");
        }
    }

    private static async Task PasteAsync(
        CancellationToken cancellationToken)
    {
        SendInput(
        [
            KeyDown(
                NativeMethods.VK_CONTROL),
            KeyDown(
                NativeMethods.VK_A),
            KeyUp(
                NativeMethods.VK_A),
            KeyUp(
                NativeMethods.VK_CONTROL)
        ]);

        await Task.Delay(
            120,
            cancellationToken);

        SendInput(
        [
            KeyDown(
                NativeMethods.VK_CONTROL),
            KeyDown(
                NativeMethods.VK_V),
            KeyUp(
                NativeMethods.VK_V),
            KeyUp(
                NativeMethods.VK_CONTROL)
        ]);

        await Task.Delay(
            200,
            cancellationToken);
    }

    private static async Task ClickRelativeAsync(
        NativeMethods.RECT rect,
        double relativeX,
        double relativeY,
        CancellationToken cancellationToken)
    {
        var x =
            rect.Left +
            (int)Math.Round(
                rect.Width *
                Math.Clamp(
                    relativeX,
                    0.0,
                    1.0));

        var y =
            rect.Top +
            (int)Math.Round(
                rect.Height *
                Math.Clamp(
                    relativeY,
                    0.0,
                    1.0));

        MoveMouseAbsolute(
            x,
            y);

        await Task.Delay(
            180,
            cancellationToken);

        ClickMouse();

        await Task.Delay(
            350,
            cancellationToken);
    }

    private static void ClickMouse()
    {
        SendInput(
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

    private static string ReadPassword()
    {
        var result =
            new StringBuilder();

        while (true)
        {
            var key =
                Console.ReadKey(
                    intercept: true);

            if (key.Key ==
                ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key ==
                ConsoleKey.Backspace)
            {
                if (result.Length > 0)
                {
                    result.Length--;

                    Console.Write(
                        "\b \b");
                }

                continue;
            }

            if (!char.IsControl(
                    key.KeyChar))
            {
                result.Append(
                    key.KeyChar);

                Console.Write(
                    "*");
            }
        }

        return result.ToString();
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

    private static void SendInput(
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
                $"Windows envió {sent}/{inputs.Length} eventos de entrada.");
        }
    }

    private static void MoveMouseAbsolute(
        int x,
        int y)
    {
        var screenWidth =
            NativeMethods.GetSystemMetrics(
                0);

        var screenHeight =
            NativeMethods.GetSystemMetrics(
                1);

        if (screenWidth <= 1 ||
            screenHeight <= 1)
        {
            throw new InvalidOperationException(
                "No se pudo obtener la resolución de pantalla.");
        }

        var absoluteX =
            (int)Math.Round(
                x *
                65535.0 /
                (screenWidth - 1));

        var absoluteY =
            (int)Math.Round(
                y *
                65535.0 /
                (screenHeight - 1));

        SendInput(
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
                                dx =
                                    absoluteX,

                                dy =
                                    absoluteY,

                                dwFlags =
                                    NativeMethods.MOUSEEVENTF_MOVE |
                                    NativeMethods.MOUSEEVENTF_ABSOLUTE
                            }
                    }
            }
        ]);
    }
}
