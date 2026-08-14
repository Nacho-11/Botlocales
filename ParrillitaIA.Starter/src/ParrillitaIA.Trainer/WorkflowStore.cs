using System.Text.Json;

namespace ParrillitaIA.Trainer;

public static class WorkflowStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Save(string path, WorkflowModel model)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(model, Options));
    }

    public static WorkflowModel Load(string path)
    {
        return JsonSerializer.Deserialize<WorkflowModel>(
            File.ReadAllText(path), Options)
            ?? throw new InvalidOperationException("El flujo JSON no es válido.");
    }

    public static string Pretty(WorkflowModel model) =>
        JsonSerializer.Serialize(model, Options);
}
