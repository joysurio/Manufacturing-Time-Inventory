namespace ManufacturingTimeTracking.Models.Templates;

public class StepTemplate
{
    public int Id { get; set; }
    public int PhaseTemplateId { get; set; }
    public int SortOrder { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public bool AllowSkip { get; set; } = true;
    // When true the step will be hidden (not shown in some UIs)
    public bool Hidden { get; set; } = false;
    // Target time for the step in seconds (optional)
    public int? TargetSeconds { get; set; }
    
    // Navigation
    public PhaseTemplate PhaseTemplate { get; set; } = null!;
    public ICollection<StepTemplateTool> Tools { get; set; } = new List<StepTemplateTool>();
    public ICollection<StepTemplateMaterial> Materials { get; set; } = new List<StepTemplateMaterial>();
    public ICollection<StepTemplateMedia> Media { get; set; } = new List<StepTemplateMedia>();
}
