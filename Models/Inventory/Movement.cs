using System.ComponentModel.DataAnnotations;

namespace ManufacturingTimeTracking.Models.Inventory;

public class Movement
{
    public int Id { get; set; }

    [MaxLength(32)]
    public string Type { get; set; } = "IN"; // IN, OUT, TRANSFER, ADJUST

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public int Quantity { get; set; } = 1;

    public int? FromLocationId { get; set; }
    public Location? FromLocation { get; set; }

    public int? ToLocationId { get; set; }
    public Location? ToLocation { get; set; }

    [MaxLength(256)]
    public string PerformedBy { get; set; } = "";

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(512)]
    public string Notes { get; set; } = "";
}
