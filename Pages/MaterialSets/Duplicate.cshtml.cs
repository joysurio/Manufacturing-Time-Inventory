using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Inventory;

namespace ManufacturingTimeTracking.Pages.MaterialSets;

public class DuplicateModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public DuplicateModel(ApplicationDbContext context) => _context = context;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null) return NotFound();

        var source = await _context.MaterialSets
            .Include(s => s.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id.Value);
        if (source == null) return NotFound();

        var newSet = new MaterialSet
        {
            Name = source.Name.TrimEnd() + " (copy)",
            Description = source.Description
        };
        _context.MaterialSets.Add(newSet);
        await _context.SaveChangesAsync();

        var sortOrder = 0;
        foreach (var item in source.Items.OrderBy(i => i.SortOrder))
        {
            _context.MaterialSetItems.Add(new MaterialSetItem
            {
                MaterialSetId = newSet.Id,
                MaterialId = item.MaterialId,
                Quantity = item.Quantity,
                SortOrder = sortOrder++
            });
        }
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Material set duplicated as \"{newSet.Name}\". You can edit the name and contents below.";
        return RedirectToPage("./Edit", new { id = newSet.Id });
    }
}
