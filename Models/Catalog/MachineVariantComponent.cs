using System.ComponentModel.DataAnnotations;
using ManufacturingTimeTracking.Models.Inventory;
using ManufacturingTimeTracking.Models.Templates;

namespace ManufacturingTimeTracking.Models.Catalog;

/// <summary>
/// Component (spare part / material) required to build one machine of a specific variant.
/// Variant-level components are added on top of model-level components when generating pick lists.
/// </summary>
public class MachineVariantComponent
{
    public int Id { get; set; }
    public int MachineVariantId { get; set; }
    /// <summary>Material required (preferred). When set, component is displayed and tracked as material.</summary>
    public int? MaterialId { get; set; }
    /// <summary>Primary item (legacy/alternative). When MaterialId is null, this is used for pick lists.</summary>
    public int? ItemId { get; set; }
    /// <summary>Quantity required per machine for this variant.</summary>
    public int Quantity { get; set; } = 1;
    [MaxLength(128)]
    public string? Notes { get; set; }

    public MachineVariant MachineVariant { get; set; } = null!;
    public Item? Item { get; set; }
    public Material? Material { get; set; }
    /// <summary>Alternative items (only when component is item-based).</summary>
    public ICollection<MachineVariantComponentAlternative> Alternatives { get; set; } = new List<MachineVariantComponentAlternative>();
}

