using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Inventory;

namespace ManufacturingTimeTracking.Pages.MaterialSets;

public class DeleteModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public DeleteModel(ApplicationDbContext context) => _context = context;

    public MaterialSet? MaterialSet { get; set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null) return NotFound();
        MaterialSet = await _context.MaterialSets
            .Include(s => s.Items)
            .ThenInclude(i => i.Material)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);
        return MaterialSet == null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (id == null) return NotFound();
        var set = await _context.MaterialSets.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == id);
        if (set == null) return NotFound();
        var name = set.Name;
        _context.MaterialSets.Remove(set);
        await _context.SaveChangesAsync();
        TempData["Success"] = $"Material set \"{name}\" deleted.";
        return RedirectToPage("./Index");
    }
}
