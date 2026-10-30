namespace ManufacturingTimeTracking.Models.Templates;

public class StepTemplateMaterial
{
    public int Id { get; set; }
    public int StepTemplateId { get; set; }
    public int MaterialId { get; set; }
    public decimal? Qty { get; set; }
    
    // Navigation
    public StepTemplate StepTemplate { get; set; } = null!;
    public Material Material { get; set; } = null!;
}
