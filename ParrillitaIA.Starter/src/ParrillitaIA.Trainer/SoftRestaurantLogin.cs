using System.Runtime.InteropServices;
using System.Text;

namespace ParrillitaIA.Trainer;

public sealed class SoftRestaurantLogin
{
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
        var loginWindow = launcher.FindLoginWindow();

        if (loginWindow == IntPtr.Zero)
        {
            Console.WriteLine("No se requiere inicio de sesión.");
            return;
        }

        if (!NativeMethods.GetWindowRect(loginWindow, out var rect) ||
            rect.Width <= 0 ||
            rect.Height <= 0)
        {
            throw new InvalidOperationException(
                "No se pudo obtener el tamaño de la ventana de inicio de sesión.");
        }

        Console.WriteLine();
        Console.WriteLine("SoftRestaurant requiere inicio de sesión.");

        var username = _settings.Username;
        string password;

        if (CredentialStore.TryRead(
                _credentialTarget,
                out var savedUsername,
                out var savedPassword))
        {
            if (string.IsNullOrWhiteSpace(username))
                username = savedUsername;

            password = savedPassword;

            Console.WriteLine(
                $"[LOGIN] Credencial protegida encontrada para {_credentialTarget}.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                Console.Write("Usuario: ");
                username = Console.ReadLine() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(username))
                throw new InvalidOperationException("El usuario no puede estar vacío.");

            Console.Write("Contraseña: ");
            password = ReadPassword();
            Console.WriteLine();
        }

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                "No hay credenciales válidas para iniciar sesión.");
        }

        NativeMethods.SetForegroundWindow(loginWindow);
        await Task.Delay(500, cancellationToken);

        Console.WriteLine("[LOGIN] Click Usuario...");
        await ClickRelativeAsync(
            rect,
            _settings.LoginUsernameX,
            _settings.LoginUsernameY,
            cancellationToken);

        Console.WriteLine("[LOGIN] Pegando usuario...");
        ClipboardHelper.SetText(username);
        await PasteAsync(cancellationToken);
        await Task.Delay(350, cancellationToken);

        Console.WriteLine("[LOGIN] Click Contraseña...");
        await ClickRelativeAsync(
            rect,
            _settings.LoginPasswordX,
            _settings.LoginPasswordY,
            cancellationToken);

        Console.WriteLine("[LOGIN] Pegando contraseña...");
        ClipboardHelper.SetText(password);
        await PasteAsync(cancellationToken);
        await Task.Delay(350, cancellationToken);

        ClipboardHelper.TryClear();

        Console.WriteLine("[LOGIN] Click INICIAR...");
        await ClickRelativeAsync(
            rect,
            _settings.LoginButtonX,
            _settings.LoginButtonY,
            cancellationToken);

        password = string.Empty;

        Console.WriteLine("[LOGIN] Esperando ventana principal...");
        await WaitForLoginToFinishAsync(launcher, cancellationToken);
    }

    private async Task WaitForLoginToFinishAsync(
        SoftRestaurantLauncher launcher,
        CancellationToken cancellationToken)
    {
        var deadline =
            DateTimeOffset.UtcNow.AddSeconds(
                Math.Clamp(_settings.LoginTimeoutSeconds, 5, 60));

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var main = launcher.FindMainWindow();

            if (main != IntPtr.Zero)
            {
                Console.WriteLine("Inicio de sesión completado.");
                NativeMethods.SetForegroundWindow(main);
                return;
            }

            await Task.Delay(300, cancellationToken);
        }

        throw new TimeoutException(
            "El formulario de inicio de sesión no avanzó a la ventana principal.");
    }

    private static async Task PasteAsync(
        CancellationToken cancellationToken)
    {
        SendInput(new[]
        {
            KeyDown(NativeMethods.VK_CONTROL),
            KeyDown(NativeMethods.VK_A),
            KeyUp(NativeMethods.VK_A),
            KeyUp(NativeMethods.VK_CONTROL)
        });

        await Task.Delay(120, cancellationToken);

        SendInput(new[]
        {
            KeyDown(NativeMethods.VK_CONTROL),
            KeyDown(NativeMethods.VK_V),
            KeyUp(NativeMethods.VK_V),
            KeyUp(NativeMethods.VK_CONTROL)
        });

        await Task.Delay(200, cancellationToken);
    }

    private static async Task ClickRelativeAsync(
        NativeMethods.RECT rect,
        double relativeX,
        double relativeY,
        CancellationToken cancellationToken)
    {
        var x = rect.Left +
            (int)Math.Round(
                rect.Width * Math.Clamp(relativeX, 0.0, 1.0));

        var y = rect.Top +
            (int)Math.Round(
                rect.Height * Math.Clamp(relativeY, 0.0, 1.0));

        MoveMouseAbsolute(x, y);

        await Task.Delay(140, cancellationToken);
        ClickMouse();
        await Task.Delay(250, cancellationToken);
    }

    private static void ClickMouse()
    {
        SendInput(new[]
        {
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                Data = new NativeMethods.INPUTUNION
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        dwFlags = NativeMethods.MOUSEEVENTF_LEFTDOWN
                    }
                }
            },
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                Data = new NativeMethods.INPUTUNION
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        dwFlags = NativeMethods.MOUSEEVENTF_LEFTUP
                    }
                }
            }
        });
    }

    private static string ReadPassword()
    {
        var result = new StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
                break;

            if (key.Key == ConsoleKey.Backspace)
            {
                if (result.Length > 0)
                {
                    result.Length--;
                    Console.Write("\b \b");
                }
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                result.Append(key.KeyChar);
                Console.Write("*");
            }
        }

        return result.ToString();
    }

    private static NativeMethods.INPUT KeyDown(ushort key) =>
        new()
        {
            type = NativeMethods.INPUT_KEYBOARD,
            Data = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT { wVk = key }
            }
        };

    private static NativeMethods.INPUT KeyUp(ushort key) =>
        new()
        {
            type = NativeMethods.INPUT_KEYBOARD,
            Data = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = key,
                    dwFlags = NativeMethods.KEYEVENTF_KEYUP
                }
            }
        };

    private static void SendInput(
        NativeMethods.INPUT[] inputs)
    {
        var sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.INPUT>());

        if (sent != inputs.Length)
        {
            throw new InvalidOperationException(
                $"Windows envió {sent}/{inputs.Length} eventos de entrada.");
        }
    }

    private static void MoveMouseAbsolute(
        int x,
        int y)
    {
        var screenWidth = NativeMethods.GetSystemMetrics(0);
        var screenHeight = NativeMethods.GetSystemMetrics(1);

        if (screenWidth <= 1 || screenHeight <= 1)
        {
            throw new InvalidOperationException(
                "No se pudo obtener la resolución de pantalla.");
        }

        var absoluteX =
            (int)Math.Round(x * 65535.0 / (screenWidth - 1));

        var absoluteY =
            (int)Math.Round(y * 65535.0 / (screenHeight - 1));

        SendInput(new[]
        {
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                Data = new NativeMethods.INPUTUNION
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        dx = absoluteX,
                        dy = absoluteY,
                        dwFlags =
                            NativeMethods.MOUSEEVENTF_MOVE |
                            NativeMethods.MOUSEEVENTF_ABSOLUTE
                    }
                }
            }
        });
    }
}
