namespace ManufacturingTimeTracking.Models.Tasks;

/// <summary>
/// A pause within a task session. EndUtc is null while paused.
/// </summary>
public class TaskSessionPause
{
    public int Id { get; set; }
    public int TaskSessionId { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }

    public TaskSession TaskSession { get; set; } = null!;
}
