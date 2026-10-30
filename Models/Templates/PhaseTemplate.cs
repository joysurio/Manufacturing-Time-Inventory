namespace ManufacturingTimeTracking.Models.Templates;

public class PhaseTemplate
{
    public int Id { get; set; }
    public int ProcessTemplateId { get; set; }
    public int SortOrder { get; set; }
    public string Name { get; set; } = string.Empty;
    
    // Navigation
    public ProcessTemplate ProcessTemplate { get; set; } = null!;
    public ICollection<StepTemplate> Steps { get; set; } = new List<StepTemplate>();
}
