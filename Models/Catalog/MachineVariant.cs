namespace ManufacturingTimeTracking.Models.Catalog;

public class MachineVariant
{
    public int Id { get; set; }
    public int MachineModelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    
    // Navigation
    public MachineModel MachineModel { get; set; } = null!;
    /// <summary>Variant-specific components (materials/items) required per machine for pick lists.</summary>
    public ICollection<MachineVariantComponent> Components { get; set; } = new List<MachineVariantComponent>();
}
