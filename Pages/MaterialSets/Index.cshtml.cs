using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Inventory;

namespace ManufacturingTimeTracking.Pages.MaterialSets;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public IndexModel(ApplicationDbContext context) => _context = context;

    public IList<MaterialSet> MaterialSets { get; set; } = default!;

    /// <summary>Computed total price per set (from materials and quantities).</summary>
    public Dictionary<int, decimal?> SetTotalPrices { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public async Task OnGetAsync()
    {
        var query = _context.MaterialSets
            .Include(s => s.Items)
            .ThenInclude(i => i.Material)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim().ToLower();
            query = query.Where(s =>
                s.Name.ToLower().Contains(term) ||
                (s.Description != null && s.Description.ToLower().Contains(term)));
        }

        MaterialSets = await query.OrderBy(s => s.Name).ToListAsync();
        SetTotalPrices = MaterialSets.ToDictionary(s => s.Id, s => ComputeSetTotalPrice(s));
    }

    /// <summary>Sum of (material unit price × quantity) for all items in the set.</summary>
    private static decimal? ComputeSetTotalPrice(MaterialSet set)
    {
        if (set.Items == null || set.Items.Count == 0) return null;
        var total = set.Items.Sum(i => (i.Material?.Price ?? 0m) * i.Quantity);
        return total;
    }
}
