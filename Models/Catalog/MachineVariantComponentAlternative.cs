using ManufacturingTimeTracking.Models.Inventory;

namespace ManufacturingTimeTracking.Models.Catalog;

/// <summary>
/// Alternative item for a variant-level component requirement.
/// </summary>
public class MachineVariantComponentAlternative
{
    public int Id { get; set; }
    public int ComponentId { get; set; }
    public int ItemId { get; set; }
    /// <summary>Display order for alternatives (lower = shown first).</summary>
    public int SortOrder { get; set; } = 0;

    public MachineVariantComponent Component { get; set; } = null!;
    public Item Item { get; set; } = null!;
}

