namespace ManufacturingTimeTracking.Models.Execution;

/// <summary>
/// Tracks how many units of a material-only component (no linked inventory item)
/// have been manually marked as picked for a build order.
/// </summary>
public class OrderMaterialPickLine
{
    public int Id { get; set; }
    public int BuildOrderId { get; set; }

    /// <summary>MachineModelComponent.Id that this line tracks. Null when line comes from a material set (ad-hoc).</summary>
    public int? ComponentId { get; set; }
    /// <summary>MachineVariantComponent.Id that this line tracks (variant-only materials). Null when not variant-scoped.</summary>
    public int? VariantComponentId { get; set; }

    public int MaterialId { get; set; }
    public int QuantityRequired { get; set; }
    public int QuantityPicked { get; set; }

    public BuildOrder BuildOrder { get; set; } = null!;
}
