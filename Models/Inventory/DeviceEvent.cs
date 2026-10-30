using System.ComponentModel.DataAnnotations;

namespace ManufacturingTimeTracking.Models.Inventory;

public class DeviceEvent
{
    public int Id { get; set; }

    [Required, MaxLength(64)]
    public string DeviceId { get; set; } = "";

    [Required, MaxLength(64)]
    public string EventType { get; set; } = "";

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public string PayloadJson { get; set; } = "{}";
}
