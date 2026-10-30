namespace ManufacturingTimeTracking.Models.Templates;

public class Tool
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    
    // Navigation
    public ICollection<StepTemplateTool> StepTemplates { get; set; } = new List<StepTemplateTool>();
}
