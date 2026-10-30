namespace ManufacturingTimeTracking.Models.Templates;

public class ProcessTemplate
{
    public int Id { get; set; }
    public int MachineModelId { get; set; }
    public int MachineVariantId { get; set; }
    
    // Navigation
    public ICollection<PhaseTemplate> Phases { get; set; } = new List<PhaseTemplate>();
}
