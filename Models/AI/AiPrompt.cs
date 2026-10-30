namespace ManufacturingTimeTracking.Models.AI;

/// <summary>
/// Stores GPT prompt templates for AI report generation.
/// Name is the key used in code (e.g. Prompt_Template_Report, Prompt_Template_Technician_Analysis).
/// </summary>
public class AiPrompt
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string PromptText { get; set; } = "";
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}
