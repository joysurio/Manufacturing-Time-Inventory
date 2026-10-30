namespace ManufacturingTimeTracking.Models.Execution;

public class BuildExecution
{
    public int Id { get; set; }
    public int BuildOrderId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public string Status { get; set; } = "InProgress"; // InProgress, Completed, Cancelled
    
    // Navigation
    public BuildOrder BuildOrder { get; set; } = null!;
    public ICollection<PhaseExec> Phases { get; set; } = new List<PhaseExec>();
}
