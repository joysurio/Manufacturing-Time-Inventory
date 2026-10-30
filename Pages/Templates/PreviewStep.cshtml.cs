using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Templates;
using ManufacturingTimeTracking.Helpers;

namespace ManufacturingTimeTracking.Pages.Templates;

public class PreviewStepModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public PreviewStepModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public StepTemplate StepTemplate { get; set; } = default!;
    public List<StepTemplateTool> StepTools { get; set; } = new();
    public List<StepTemplateMaterial> StepMaterials { get; set; } = new();
    public List<StepTemplateMedia> StepMedia { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int? stepId)
    {
        if (stepId == null)
        {
            return NotFound();
        }

        var step = await _context.StepTemplates
            .Include(s => s.Tools)
                .ThenInclude(st => st.Tool)
            .Include(s => s.Materials)
                .ThenInclude(sm => sm.Material)
            .Include(s => s.Media)
                .ThenInclude(sm => sm.Media)
            .FirstOrDefaultAsync(s => s.Id == stepId);

        if (step == null)
        {
            return NotFound();
        }

        StepTemplate = step;
        StepTools = step.Tools.ToList();
        StepMaterials = step.Materials.ToList();
        StepMedia = step.Media.ToList();

        return Page();
    }
}
