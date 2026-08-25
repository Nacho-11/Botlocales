namespace ParrillitaIA.Trainer;

public sealed class WorkflowModel
{
    public int Version { get; init; } = 36;
    public string Local { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string TargetProcessName { get; init; } = string.Empty;
    public DateTimeOffset TrainedAt { get; init; } = DateTimeOffset.Now;
    public List<WorkflowStep> Steps { get; init; } = [];
}

public sealed record WorkflowStep
{
    public int Order { get; init; }

    // LeftClick | KeyPress | WaitForWindow | SetYesterdayDate
    public string Action { get; init; } = "LeftClick";

    public int DelayBeforeMs { get; init; }

    public string ProcessName { get; init; } = string.Empty;
    public string WindowTitle { get; init; } = string.Empty;
    public string StableTitle { get; init; } = string.Empty;
    public string WindowClass { get; init; } = string.Empty;

    public int ScreenX { get; init; }
    public int ScreenY { get; init; }
    public double RelativeX { get; init; }
    public double RelativeY { get; init; }

    public ushort VirtualKey { get; init; }
    public bool Ctrl { get; init; }
    public bool Shift { get; init; }
    public bool Alt { get; init; }

    public string ValueFormat { get; init; } = "dd/MM/yyyy";

    public int RecordedWindowWidth { get; init; }
    public int RecordedWindowHeight { get; init; }

    public int WindowWaitTimeoutMs { get; init; } = 30000;
}
