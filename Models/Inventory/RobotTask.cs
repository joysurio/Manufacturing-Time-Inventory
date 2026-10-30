using System.ComponentModel.DataAnnotations;

namespace ManufacturingTimeTracking.Models.Inventory;

public class RobotTask
{
    public int Id { get; set; }

    [Required, MaxLength(16)]
    public string TaskType { get; set; } = "PUTAWAY";

    public int? ItemId { get; set; }
    public Item? Item { get; set; }

    public int? FromLocationId { get; set; }
    public Location? FromLocation { get; set; }

    public int? ToLocationId { get; set; }
    public Location? ToLocation { get; set; }

    [Required, MaxLength(16)]
    public string Status { get; set; } = "CREATED";

    public int Priority { get; set; } = 5;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }

    [MaxLength(512)]
    public string? ErrorMessage { get; set; }
}
