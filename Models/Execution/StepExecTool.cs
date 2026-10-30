namespace ManufacturingTimeTracking.Models.Execution;

public class StepExecTool
{
    public int Id { get; set; }
    public int StepExecId { get; set; }
    public string ToolName { get; set; } = string.Empty; // Snapshot as text
    
    // Navigation
    public StepExec StepExec { get; set; } = null!;
}
