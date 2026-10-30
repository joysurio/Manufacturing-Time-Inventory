namespace ManufacturingTimeTracking.Models.Tasks;

/// <summary>
/// One work session on a task: from Start to Finish, with optional pauses.
/// </summary>
public class TaskSession
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public string UserId { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }

    public TaskItem Task { get; set; } = null!;
    public ICollection<TaskSessionPause> Pauses { get; set; } = new List<TaskSessionPause>();
}
