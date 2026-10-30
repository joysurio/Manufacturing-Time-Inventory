using ManufacturingTimeTracking.Models.Catalog;

namespace ManufacturingTimeTracking.Models.Execution;

public class StepRun
{
    public int Id { get; set; }
    public int StepExecId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public int? DurationSeconds { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int? WorkstationId { get; set; }
    public string? Note { get; set; }
    
    // Navigation
    public StepExec StepExec { get; set; } = null!;
    public Workstation? Workstation { get; set; }
}
