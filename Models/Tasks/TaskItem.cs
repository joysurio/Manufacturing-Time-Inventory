namespace ManufacturingTimeTracking.Models.Tasks;

/// <summary>
/// Internal task (e.g. Cleaning, Customer visit, Maintenance) not linked to an order.
/// </summary>
public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>User assigned to this task.</summary>
    public string AssignedUserId { get; set; } = "";
    public TaskItemStatus Status { get; set; } = TaskItemStatus.Open;
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<TaskSession> Sessions { get; set; } = new List<TaskSession>();
}

public enum TaskItemStatus
{
    Open = 0,
    InProgress = 1,
    Finished = 2
}
