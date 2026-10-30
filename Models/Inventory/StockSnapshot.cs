namespace ManufacturingTimeTracking.Models.Inventory;

public class StockSnapshot
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public int LocationId { get; set; }
    public Location? Location { get; set; }

    public int Quantity { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
