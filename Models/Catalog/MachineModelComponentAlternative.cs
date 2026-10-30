using ManufacturingTimeTracking.Models.Inventory;

namespace ManufacturingTimeTracking.Models.Catalog;

/// <summary>
/// Alternative item for a component requirement. When picking, any of the alternatives can be used.
/// Example: Component requires "Computer", alternatives are "Computer Model 1" OR "Computer Model 2".
/// </summary>
public class MachineModelComponentAlternative
{
    public int Id { get; set; }
    public int ComponentId { get; set; }
    public int ItemId { get; set; }
    /// <summary>Display order for alternatives (lower = shown first).</summary>
    public int SortOrder { get; set; } = 0;

    public MachineModelComponent Component { get; set; } = null!;
    public Item Item { get; set; } = null!;
}
