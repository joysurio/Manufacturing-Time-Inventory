namespace ManufacturingTimeTracking.Models.Execution;

public class StepExec
{
    public int Id { get; set; }
    public int PhaseExecId { get; set; }
        // Link back to the template step when execution was created (nullable)
        public int? StepTemplateId { get; set; }
    public int SortOrder { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public bool AllowSkip { get; set; } = true;
    // When true the step will be hidden from execution UI
    public bool Hidden { get; set; } = false;
    public string State { get; set; } = "Pending"; // Pending, InProgress, Paused, Done, Skipped
    public string? SkipReason { get; set; }
    // Target time snapshot (seconds) copied from StepTemplate when execution was created or backfilled
    public int? TargetSeconds { get; set; }
    
    // Navigation
    public PhaseExec PhaseExec { get; set; } = null!;
    public ICollection<StepExecTool> Tools { get; set; } = new List<StepExecTool>();
    public ICollection<StepExecMaterial> Materials { get; set; } = new List<StepExecMaterial>();
    public ICollection<StepRun> Runs { get; set; } = new List<StepRun>();
    public ICollection<StepEvidence> Evidence { get; set; } = new List<StepEvidence>();
}
