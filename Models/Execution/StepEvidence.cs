using ManufacturingTimeTracking.Models.Templates;

namespace ManufacturingTimeTracking.Models.Execution;

public class StepEvidence
{
    public int Id { get; set; }
    public int StepExecId { get; set; }
    public int MediaId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string UserId { get; set; } = string.Empty;
    
    // Navigation
    public StepExec StepExec { get; set; } = null!;
    public Media Media { get; set; } = null!;
}
