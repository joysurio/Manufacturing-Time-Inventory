namespace ManufacturingTimeTracking.Models.Inventory;

/// <summary>Stock quantity of a material at a location (receiving without creating Items).</summary>
public class MaterialStock
{
    public int Id { get; set; }
    public int MaterialId { get; set; }
    public int LocationId { get; set; }
    public int Quantity { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ManufacturingTimeTracking.Models.Templates.Material? Material { get; set; }
    public Location? Location { get; set; }
}
