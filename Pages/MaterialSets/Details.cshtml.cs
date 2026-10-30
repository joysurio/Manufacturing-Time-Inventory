using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Inventory;

namespace ManufacturingTimeTracking.Pages.MaterialSets;

public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public DetailsModel(ApplicationDbContext context) => _context = context;

    public MaterialSet? MaterialSet { get; set; }

    /// <summary>Total price of the set (sum of material unit price × quantity).</summary>
    public decimal? TotalPrice { get; set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null) return NotFound();
        MaterialSet = await _context.MaterialSets
            .Include(s => s.Items)
            .ThenInclude(i => i.Material)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);
        if (MaterialSet == null) return NotFound();
        TotalPrice = ComputeSetTotalPrice(MaterialSet);
        return Page();
    }

    private static decimal? ComputeSetTotalPrice(MaterialSet set)
    {
        if (set.Items == null || set.Items.Count == 0) return null;
        return set.Items.Sum(i => (i.Material?.Price ?? 0m) * i.Quantity);
    }
}
