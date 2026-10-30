using System.ComponentModel.DataAnnotations;
using ManufacturingTimeTracking.Models.Inventory;
using ManufacturingTimeTracking.Models.Templates;

namespace ManufacturingTimeTracking.Models.Catalog;

/// <summary>
/// Component (spare part) required to build one machine of this model. Used for pick lists when creating orders.
/// Can be a Material (preferred) or an Item. If Alternatives exist (item-based), any of those items can be picked instead of the primary ItemId.
/// </summary>
public class MachineModelComponent
{
    public int Id { get; set; }
    public int MachineModelId { get; set; }
    /// <summary>Material required (preferred). When set, component is displayed and tracked as material.</summary>
    public int? MaterialId { get; set; }
    /// <summary>Primary item (legacy/alternative). When MaterialId is null, this is used for pick lists.</summary>
    public int? ItemId { get; set; }
    /// <summary>Quantity required per machine (e.g. 2 motors, 1 PC).</summary>
    public int Quantity { get; set; } = 1;
    [MaxLength(128)]
    public string? Notes { get; set; }

    public MachineModel MachineModel { get; set; } = null!;
    public Item? Item { get; set; }
    public Material? Material { get; set; }
    /// <summary>Alternative items (only when component is item-based).</summary>
    public ICollection<MachineModelComponentAlternative> Alternatives { get; set; } = new List<MachineModelComponentAlternative>();
}
