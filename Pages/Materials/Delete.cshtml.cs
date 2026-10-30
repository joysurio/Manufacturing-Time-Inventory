using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Templates;

namespace ManufacturingTimeTracking.Pages.Materials;

public class DeleteModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public DeleteModel(ApplicationDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public Material Material { get; set; } = default!;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var material = await _context.Materials.FirstOrDefaultAsync(m => m.Id == id);

        if (material == null)
        {
            return NotFound();
        }

        Material = material;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var material = await _context.Materials.FindAsync(id);
        if (material == null)
        {
            return RedirectToPage("./Index");
        }

        // Check DB directly for references (avoids stale or missing Includes)
        var usedInTemplates = await _context.StepTemplateMaterials
            .AnyAsync(sm => sm.MaterialId == id.Value);
        if (usedInTemplates)
        {
            var count = await _context.StepTemplateMaterials.CountAsync(sm => sm.MaterialId == id.Value);
            TempData["DeleteError"] = $"Cannot delete \"{material.Name}\" because it is used in {count} process template step(s). Remove it from all templates first.";
            return RedirectToPage("./Index");
        }

        var linkedItems = await _context.Items
            .CountAsync(i => i.MaterialId == id.Value);
        if (linkedItems > 0)
        {
            TempData["DeleteError"] = $"Cannot delete \"{material.Name}\" because {linkedItems} inventory item(s) are linked to it. Unlink those items first.";
            return RedirectToPage("./Index");
        }

        try
        {
            _context.Materials.Remove(material);
            await _context.SaveChangesAsync();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (ex.InnerException?.Message?.Contains("REFERENCE") == true)
        {
            TempData["DeleteError"] = $"Cannot delete \"{material.Name}\" because it is still referenced elsewhere (e.g. in process templates or inventory). Remove those references first.";
            return RedirectToPage("./Index");
        }

        return RedirectToPage("./Index");
    }
}
