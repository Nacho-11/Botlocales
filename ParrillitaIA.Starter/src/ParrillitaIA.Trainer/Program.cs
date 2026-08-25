using ParrillitaIA.Trainer;

if (args.Length < 3)
{
    Console.WriteLine(
        "Uso: ParrillitaIA.Trainer train|test|run|show|calibrate-login|save-credentials LOCAL FLUJO");

    return 2;
}

var command = args[0].Trim().ToLowerInvariant();
var local = WorkflowName.Sanitize(args[1]);
var workflow = WorkflowName.Sanitize(args[2]);

var directory =
    Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData),
        "ParrillitaIA",
        "Training",
        local);

Directory.CreateDirectory(directory);

var file =
    Path.Combine(
        directory,
        $"{workflow}.json");

var settingsPath =
    Path.Combine(
        AppContext.BaseDirectory,
        "trainer.settings.json");

var settings =
    TrainerSettings.Load(
        AppContext.BaseDirectory);

var credentialTarget =
    $"ParrillitaIA.SoftRestaurant.{local}";

try
{
    if (command == "save-credentials")
    {
        Console.WriteLine("=== PARRILLITA IA V3.6.6 - CREDENCIALES ===");
        Console.Write("Usuario SoftRestaurant: ");
        var username = Console.ReadLine() ?? string.Empty;

        Console.Write("Contraseña SoftRestaurant: ");
        var password = ReadSecret();
        Console.WriteLine();

        CredentialStore.Save(
            credentialTarget,
            username,
            password);

        password = string.Empty;

        Console.WriteLine(
            $"Credencial guardada de forma protegida en Windows: {credentialTarget}");

        return 0;
    }

    if (command == "calibrate-login")
    {
        Console.WriteLine("=== PARRILLITA IA TRAINER V3.6.6 ===");
        Console.WriteLine();
        Console.WriteLine("Preparando SoftRestaurant para calibrar login...");

        var launcher =
            new SoftRestaurantLauncher(
                settings.SoftRestaurant);

        await launcher.EnsureProcessRunningAsync(
            CancellationToken.None);

        if (launcher.FindLoginWindow() == IntPtr.Zero)
        {
            Console.WriteLine(
                "SoftRestaurant no está mostrando Inicio de sesión.");

            return 4;
        }

        using var calibrator =
            new LoginCalibrator(
                launcher,
                settingsPath);

        calibrator.Run();

        Console.WriteLine(
            "Calibración finalizada.");

        return 0;
    }

    if (command == "train")
    {
        Console.WriteLine("=== PARRILLITA IA TRAINER V3.6.6 ===");
        Console.WriteLine();
        Console.WriteLine("Preparando SoftRestaurant...");

        var launcher =
            new SoftRestaurantLauncher(
                settings.SoftRestaurant);

        await launcher.EnsureProcessRunningAsync(
            CancellationToken.None);

        var login =
            new SoftRestaurantLogin(
                settings.SoftRestaurant,
                credentialTarget);

        await login.LoginIfNeededAsync(
            launcher,
            CancellationToken.None);

        await launcher.WaitUntilReadyForAutomationAsync(
            CancellationToken.None);

        Console.WriteLine(
            "SoftRestaurant listo.");

        using var recorder =
            new WorkflowRecorder(
                local,
                workflow);

        var model =
            recorder.Record();

        WorkflowStore.Save(
            file,
            model);

        Console.WriteLine();
        Console.WriteLine(
            $"Flujo guardado: {file}");

        Console.WriteLine(
            $"Proceso objetivo: {model.TargetProcessName}");

        Console.WriteLine(
            $"Pasos: {model.Steps.Count}");

        return 0;
    }

    if (!File.Exists(file))
    {
        Console.WriteLine(
            $"No existe el flujo: {file}");

        return 3;
    }

    var loaded =
        WorkflowStore.Load(
            file);

    if (command == "show")
    {
        Console.WriteLine(
            WorkflowStore.Pretty(
                loaded));

        return 0;
    }

    if (command is "test" or "run")
    {
        Console.WriteLine("=== PARRILLITA IA TRAINER V3.6.6 ===");
        Console.WriteLine($"Flujo: {loaded.Local}/{loaded.Name}");
        Console.WriteLine($"Proceso: {loaded.TargetProcessName}");
        Console.WriteLine($"Pasos: {loaded.Steps.Count}");
        Console.WriteLine();
        Console.WriteLine("Preparando SoftRestaurant...");

        var launcher =
            new SoftRestaurantLauncher(
                settings.SoftRestaurant);

        await launcher.EnsureProcessRunningAsync(
            CancellationToken.None);

        var login =
            new SoftRestaurantLogin(
                settings.SoftRestaurant,
                credentialTarget);

        await login.LoginIfNeededAsync(
            launcher,
            CancellationToken.None);

        await launcher.WaitUntilReadyForAutomationAsync(
            CancellationToken.None);

        if (command == "test")
        {
            Console.WriteLine(
                "MODO TEST: inicia en 3 segundos.");

            await Task.Delay(
                3000);
        }

        await new WorkflowRunner()
            .RunAsync(
                loaded,
                CancellationToken.None);

        Console.WriteLine();
        Console.WriteLine(
            "Flujo finalizado.");

        return 0;
    }

    Console.WriteLine(
        $"Comando no reconocido: {command}");

    return 2;
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("ERROR:");
    Console.WriteLine(ex.Message);

    return 10;
}

static string ReadSecret()
{
    var result =
        new System.Text.StringBuilder();

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
                Console.Write("\b \b");
            }

            continue;
        }

        if (!char.IsControl(
                key.KeyChar))
        {
            result.Append(
                key.KeyChar);

            Console.Write("*");
        }
    }

    return result.ToString();
}
