using ManufacturingTimeTracking.Models.Templates;

namespace ManufacturingTimeTracking.Models.Inventory;

/// <summary>
/// A predefined group of materials. Used to add multiple materials at once in material outputs and pick lists.
/// </summary>
public class MaterialSet
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<MaterialSetItem> Items { get; set; } = new List<MaterialSetItem>();

    public decimal? TotalPrice =>
        Items?.Any() == true
            ? Items.Sum(i =>
                (i.Material?.Price ?? 0m) * i.Quantity)
            : null;
}
