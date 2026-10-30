namespace ManufacturingTimeTracking.Models.Templates;

public class StepTemplateMedia
{
    public int Id { get; set; }
    public int StepTemplateId { get; set; }
    public int MediaId { get; set; }
    public string? Caption { get; set; }
    
    // Navigation
    public StepTemplate StepTemplate { get; set; } = null!;
    public Media Media { get; set; } = null!;
}
