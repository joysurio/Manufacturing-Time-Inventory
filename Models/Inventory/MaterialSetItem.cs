using ManufacturingTimeTracking.Models.Templates;

namespace ManufacturingTimeTracking.Models.Inventory;

/// <summary>
/// One material and its quantity within a material set.
/// </summary>
public class MaterialSetItem
{
    public int Id { get; set; }
    public int MaterialSetId { get; set; }
    public int MaterialId { get; set; }
    public int Quantity { get; set; } = 1;
    public int SortOrder { get; set; }

    public MaterialSet MaterialSet { get; set; } = null!;
    public Material Material { get; set; } = null!;
}
