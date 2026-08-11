using ParrillitaIA.Trainer;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("ParrillitaIA.Trainer solo funciona en Windows.");
    return 1;
}

if (args.Length < 3)
{
    Usage();
    return 2;
}

var command = args[0].Trim().ToLowerInvariant();
var local = WorkflowName.Sanitize(args[1]);
var workflow = WorkflowName.Sanitize(args[2]);

var baseDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "ParrillitaIA",
    "Training",
    local);

Directory.CreateDirectory(baseDirectory);

var workflowFile = Path.Combine(baseDirectory, $"{workflow}.json");

try
{
    switch (command)
    {
        case "train":
        {
            Console.WriteLine("=== PARRILLITA IA - MODO APRENDIZAJE ===");
            Console.WriteLine($"Local: {local}");
            Console.WriteLine($"Flujo: {workflow}");
            Console.WriteLine($"Archivo: {workflowFile}");
            Console.WriteLine();
            Console.WriteLine("Controles globales:");
            Console.WriteLine("  CTRL + SHIFT + F8  -> iniciar grabación");
            Console.WriteLine("  CTRL + SHIFT + F9  -> finalizar y guardar");
            Console.WriteLine();
            Console.WriteLine("El Trainer registra SOLO clics del mouse.");
            Console.WriteLine("No captura contraseñas ni texto escrito.");
            Console.WriteLine();
            Console.WriteLine("Abre SoftRestaurant y prepara la pantalla inicial.");
            Console.WriteLine("Cuando estés listo, pulsa CTRL + SHIFT + F8.");

            using var recorder = new WorkflowRecorder(local, workflow);
            var model = recorder.Record();

            WorkflowStore.Save(workflowFile, model);

            Console.WriteLine();
            Console.WriteLine($"Flujo guardado correctamente: {workflowFile}");
            Console.WriteLine($"Pasos registrados: {model.Steps.Count}");
            return 0;
        }

        case "test":
        case "run":
        {
            if (!File.Exists(workflowFile))
            {
                Console.Error.WriteLine($"No existe el flujo: {workflowFile}");
                return 3;
            }

            var model = WorkflowStore.Load(workflowFile);
            var runner = new WorkflowRunner();

            Console.WriteLine($"Flujo: {model.Local}/{model.Name}");
            Console.WriteLine($"Pasos: {model.Steps.Count}");

            if (command == "test")
            {
                Console.WriteLine();
                Console.WriteLine("MODO TEST:");
                Console.WriteLine("Se mostrará cada acción y tendrás 3 segundos antes de iniciar.");
                Console.WriteLine("Pulsa CTRL+C para cancelar.");
                await Task.Delay(TimeSpan.FromSeconds(3));
            }

            await runner.RunAsync(model, CancellationToken.None);

            Console.WriteLine();
            Console.WriteLine("Flujo finalizado.");
            return 0;
        }

        case "show":
        {
            if (!File.Exists(workflowFile))
            {
                Console.Error.WriteLine($"No existe el flujo: {workflowFile}");
                return 3;
            }

            var model = WorkflowStore.Load(workflowFile);
            Console.WriteLine(WorkflowStore.ToPrettyJson(model));
            return 0;
        }

        default:
            Usage();
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("ERROR:");
    Console.Error.WriteLine(ex.Message);
    return 10;
}

static void Usage()
{
    Console.WriteLine("""
ParrillitaIA.Trainer

Uso:
  ParrillitaIA.Trainer train <LOCAL> <FLUJO>
  ParrillitaIA.Trainer test  <LOCAL> <FLUJO>
  ParrillitaIA.Trainer run   <LOCAL> <FLUJO>
  ParrillitaIA.Trainer show  <LOCAL> <FLUJO>

Ejemplos:
  ParrillitaIA.Trainer train SABANA CIERRES
  ParrillitaIA.Trainer test  SABANA CIERRES
  ParrillitaIA.Trainer run   SABANA CIERRES

Durante train:
  CTRL + SHIFT + F8 = empezar
  CTRL + SHIFT + F9 = terminar y guardar
""");
}
