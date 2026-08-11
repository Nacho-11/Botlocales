namespace ParrillitaIA.Trainer;

public sealed class WorkflowModel
{
    public int Version { get; init; } = 1;
    public string Local { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DateTimeOffset TrainedAt { get; init; } = DateTimeOffset.Now;
    public int RecordedScreenWidth { get; init; }
    public int RecordedScreenHeight { get; init; }
    public List<WorkflowStep> Steps { get; init; } = [];
}

public sealed class WorkflowStep
{
    public int Order { get; init; }

    // Tiempo que transcurrió desde el clic anterior.
    public int DelayBeforeMs { get; init; }

    public string Action { get; init; } = "LeftClick";

    // Ventana que estaba activa al producirse el clic.
    public string WindowTitle { get; init; } = string.Empty;
    public string WindowClass { get; init; } = string.Empty;

    // Coordenadas absolutas de respaldo.
    public int ScreenX { get; init; }
    public int ScreenY { get; init; }

    // Coordenadas relativas a la ventana.
    // Se usan preferentemente durante reproducción.
    public double RelativeX { get; init; }
    public double RelativeY { get; init; }

    public int RecordedWindowLeft { get; init; }
    public int RecordedWindowTop { get; init; }
    public int RecordedWindowWidth { get; init; }
    public int RecordedWindowHeight { get; init; }
}
