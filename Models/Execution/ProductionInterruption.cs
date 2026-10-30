using System.ComponentModel.DataAnnotations;

namespace ManufacturingTimeTracking.Models.Execution;

/// <summary>Category of production interruption (issue #10).</summary>
public enum ProductionInterruptionCategory
{
    Blocked = 0,
    StepStopped = 1,
    Paused = 2,
    Reopened = 3
}

public class ProductionInterruption
{
    public int Id { get; set; }
    public int BuildOrderId { get; set; }
    /// <summary>Optional: when interruption is tied to a specific step (e.g. Reopened).</summary>
    public int? StepExecId { get; set; }
    public ProductionInterruptionCategory Category { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    [Required]
    public string Notes { get; set; } = string.Empty;

    public string CreatedByUserId { get; set; } = string.Empty;

    public BuildOrder BuildOrder { get; set; } = null!;
    public StepExec? StepExec { get; set; }
}
