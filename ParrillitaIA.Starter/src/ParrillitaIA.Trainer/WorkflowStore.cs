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

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(model, Options));
        File.Move(temp, path, overwrite: true);
    }

    public static WorkflowModel Load(string path)
    {
        var json = File.ReadAllText(path);

        return JsonSerializer.Deserialize<WorkflowModel>(json, Options)
               ?? throw new InvalidOperationException("El flujo JSON no es válido.");
    }

    public static string ToPrettyJson(WorkflowModel model) =>
        JsonSerializer.Serialize(model, Options);
}
