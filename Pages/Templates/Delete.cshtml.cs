using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Templates;
using ManufacturingTimeTracking.Models.Catalog;

namespace ManufacturingTimeTracking.Pages.Templates;

[Authorize(Policy = "CanEditSteps")]
public class DeleteModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public DeleteModel(ApplicationDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public ProcessTemplate ProcessTemplate { get; set; } = default!;

    public MachineModel? MachineModel { get; set; }
    public MachineVariant? MachineVariant { get; set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var processTemplate = await _context.ProcessTemplates
            .Include(pt => pt.Phases)
                .ThenInclude(p => p.Steps)
            .FirstOrDefaultAsync(pt => pt.Id == id);

        if (processTemplate == null)
        {
            return NotFound();
        }

        ProcessTemplate = processTemplate;

        // Load related MachineModel and MachineVariant
        MachineModel = await _context.MachineModels.FindAsync(processTemplate.MachineModelId);
        MachineVariant = await _context.MachineVariants.FindAsync(processTemplate.MachineVariantId);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var processTemplate = await _context.ProcessTemplates
            .Include(pt => pt.Phases)
                .ThenInclude(p => p.Steps)
            .FirstOrDefaultAsync(pt => pt.Id == id);

        if (processTemplate != null)
        {
            _context.ProcessTemplates.Remove(processTemplate);
            await _context.SaveChangesAsync();
        }

        return RedirectToPage("./Index");
    }
}
