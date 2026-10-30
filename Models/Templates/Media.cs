using ManufacturingTimeTracking.Models.Execution;

namespace ManufacturingTimeTracking.Models.Templates;

public class Media
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty; // "Image" or "Video"
    public string UrlOrPath { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public ICollection<StepTemplateMedia> StepTemplates { get; set; } = new List<StepTemplateMedia>();
    public ICollection<StepEvidence> StepEvidence { get; set; } = new List<StepEvidence>();
}
