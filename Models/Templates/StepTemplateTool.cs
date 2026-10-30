namespace ManufacturingTimeTracking.Models.Templates;

public class StepTemplateTool
{
    public int Id { get; set; }
    public int StepTemplateId { get; set; }
    public int ToolId { get; set; }
    
    // Navigation
    public StepTemplate StepTemplate { get; set; } = null!;
    public Tool Tool { get; set; } = null!;
}
