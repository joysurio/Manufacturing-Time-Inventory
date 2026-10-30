namespace ManufacturingTimeTracking.Models.Execution;

public class PhaseExec
{
    public int Id { get; set; }
    public int BuildExecutionId { get; set; }
    public int SortOrder { get; set; }
    public string Name { get; set; } = string.Empty;
    
    // Navigation
    public BuildExecution BuildExecution { get; set; } = null!;
    public ICollection<StepExec> Steps { get; set; } = new List<StepExec>();
}
