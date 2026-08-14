using System.Diagnostics;

namespace ParrillitaIA.Trainer;

public sealed class SoftRestaurantLauncher
{
    private readonly SoftRestaurantSettings _settings;

    public SoftRestaurantLauncher(
        SoftRestaurantSettings settings) =>
        _settings = settings;

    public async Task EnsureProcessRunningAsync(
        CancellationToken cancellationToken)
    {
        var existing =
            Process.GetProcesses()
                .FirstOrDefault(
                    p => string.Equals(
                        p.ProcessName,
                        _settings.ProcessName,
                        StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            if (!File.Exists(
                    _settings.ExecutablePath))
            {
                throw new FileNotFoundException(
                    $"No existe SoftRestaurant en: {_settings.ExecutablePath}");
            }

            Console.WriteLine(
                $"Abriendo SoftRestaurant: {_settings.ExecutablePath}");

            var process =
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName =
                            _settings.ExecutablePath,

                        WorkingDirectory =
                            Path.GetDirectoryName(
                                _settings.ExecutablePath)
                            ?? Environment.CurrentDirectory,

                        UseShellExecute =
                            true
                    });

            if (process is null)
            {
                throw new InvalidOperationException(
                    "Windows no pudo iniciar SoftRestaurant.");
            }

            Console.WriteLine(
                $"SoftRestaurant iniciado. PID={process.Id}");
        }
        else
        {
            Console.WriteLine(
                $"SoftRestaurant ya está abierto. PID={existing.Id}");
        }

        await WaitForStartupWindowAsync(
            cancellationToken);
    }

    public IntPtr FindLoginWindow() =>
        WindowInfo.FindWindowByProcessAndTitle(
            _settings.ProcessName,
            _settings.LoginWindowTitle);

    public IntPtr FindMainWindow() =>
        WindowInfo.FindWindowByProcessAndTitle(
            _settings.ProcessName,
            _settings.StableWindowTitle);

    public async Task<IntPtr> WaitForMainWindowAsync(
        CancellationToken cancellationToken)
    {
        var deadline =
            DateTimeOffset.UtcNow.AddSeconds(
                Math.Clamp(
                    _settings.LaunchTimeoutSeconds,
                    5,
                    120));

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var handle =
                FindMainWindow();

            if (handle != IntPtr.Zero)
            {
                Console.WriteLine(
                    "Ventana principal de SoftRestaurant detectada.");

                NativeMethods.SetForegroundWindow(
                    handle);

                return handle;
            }

            await Task.Delay(
                300,
                cancellationToken);
        }

        throw new TimeoutException(
            "No apareció la ventana principal de SoftRestaurant.");
    }

    /// <summary>
    /// Espera a que SoftRestaurant esté realmente listo para automatización.
    /// No se basa únicamente en un Delay fijo.
    /// </summary>
    public async Task<IntPtr> WaitUntilReadyForAutomationAsync(
        CancellationToken cancellationToken)
    {
        var handle =
            await WaitForMainWindowAsync(
                cancellationToken);

        Console.WriteLine(
            "Esperando que SoftRestaurant termine su inicialización...");

        var process =
            Process.GetProcesses()
                .FirstOrDefault(
                    p => string.Equals(
                        p.ProcessName,
                        _settings.ProcessName,
                        StringComparison.OrdinalIgnoreCase));

        if (process is null)
        {
            throw new InvalidOperationException(
                "El proceso de SoftRestaurant desapareció.");
        }

        // WaitForInputIdle es útil para aplicaciones Win32/VB6:
        // esperamos a que el proceso llegue al message loop antes de medir estabilidad.
        try
        {
            await Task.Run(
                () =>
                {
                    try
                    {
                        process.WaitForInputIdle(
                            15000);
                    }
                    catch
                    {
                        // Algunas aplicaciones legacy no soportan bien WaitForInputIdle.
                        // No abortamos: continuamos con las comprobaciones siguientes.
                    }
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        const int sampleMs = 500;
        const int requiredStableSamples = 12; // 6 segundos seguidos estable.
        const double maxCpuPercent = 12.0;

        var stableSamples = 0;
        var previousCpu =
            process.TotalProcessorTime;

        var previousTime =
            DateTimeOffset.UtcNow;

        var previousWindowSet =
            GetVisibleWindowSignature();

        var overallDeadline =
            DateTimeOffset.UtcNow.AddSeconds(90);

        while (DateTimeOffset.UtcNow < overallDeadline)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            await Task.Delay(
                sampleMs,
                cancellationToken);

            process.Refresh();

            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    "SoftRestaurant se cerró durante la inicialización.");
            }

            handle =
                FindMainWindow();

            if (handle == IntPtr.Zero ||
                !NativeMethods.IsWindowVisible(handle) ||
                !NativeMethods.IsWindowEnabled(handle))
            {
                stableSamples = 0;
                Console.WriteLine(
                    "Carga: esperando ventana principal habilitada...");
                continue;
            }

            var now =
                DateTimeOffset.UtcNow;

            var cpu =
                process.TotalProcessorTime;

            var elapsedMs =
                Math.Max(
                    1,
                    (now - previousTime)
                    .TotalMilliseconds);

            var cpuMs =
                (cpu - previousCpu)
                .TotalMilliseconds;

            var cpuPercent =
                cpuMs /
                elapsedMs /
                Math.Max(
                    1,
                    Environment.ProcessorCount) *
                100.0;

            previousCpu =
                cpu;

            previousTime =
                now;

            var currentWindowSet =
                GetVisibleWindowSignature();

            var windowsStable =
                string.Equals(
                    currentWindowSet,
                    previousWindowSet,
                    StringComparison.Ordinal);

            previousWindowSet =
                currentWindowSet;

            var responsive =
                NativeMethods.SendMessageTimeout(
                    handle,
                    NativeMethods.WM_NULL,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    NativeMethods.SMTO_ABORTIFHUNG,
                    1000,
                    out _)
                != IntPtr.Zero;

            var sampleStable =
                windowsStable &&
                responsive &&
                cpuPercent <= maxCpuPercent;

            if (sampleStable)
            {
                stableSamples++;

                Console.WriteLine(
                    $"Carga lista {stableSamples}/{requiredStableSamples} " +
                    $"CPU={cpuPercent:0.0}%");
            }
            else
            {
                stableSamples = 0;

                Console.WriteLine(
                    $"SoftRestaurant aún cargando... " +
                    $"CPU={cpuPercent:0.0}% " +
                    $"VentanasEstables={windowsStable} " +
                    $"Responde={responsive}");
            }

            if (stableSamples >=
                requiredStableSamples)
            {
                // Último margen corto para que el control activo se estabilice.
                NativeMethods.SetForegroundWindow(
                    handle);

                await Task.Delay(
                    1000,
                    cancellationToken);

                Console.WriteLine(
                    "SoftRestaurant confirmado como listo para automatización.");

                return handle;
            }
        }

        throw new TimeoutException(
            "SoftRestaurant no alcanzó un estado estable en 90 segundos.");
    }

    private string GetVisibleWindowSignature()
    {
        var windows =
            WindowInfo.GetVisibleWindowsForProcess(
                _settings.ProcessName);

        return string.Join(
            "|",
            windows
                .OrderBy(
                    x => x.Handle.ToInt64())
                .Select(
                    x =>
                        $"{x.Handle.ToInt64():X}:" +
                        $"{x.ClassName}:" +
                        $"{TitleMatcher.BuildStableTitle(x.Title)}:" +
                        $"{x.Width}x{x.Height}"));
    }

    private async Task WaitForStartupWindowAsync(
        CancellationToken cancellationToken)
    {
        var deadline =
            DateTimeOffset.UtcNow.AddSeconds(
                Math.Clamp(
                    _settings.LaunchTimeoutSeconds,
                    5,
                    120));

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var login =
                FindLoginWindow();

            if (login !=
                IntPtr.Zero)
            {
                Console.WriteLine(
                    "Ventana de inicio de sesión detectada.");

                NativeMethods.SetForegroundWindow(
                    login);

                return;
            }

            var main =
                FindMainWindow();

            if (main !=
                IntPtr.Zero)
            {
                Console.WriteLine(
                    "SoftRestaurant ya tiene sesión iniciada.");

                NativeMethods.SetForegroundWindow(
                    main);

                return;
            }

            await Task.Delay(
                500,
                cancellationToken);
        }

        throw new TimeoutException(
            "SoftRestaurant abrió pero no apareció el login ni la ventana principal.");
    }
}
