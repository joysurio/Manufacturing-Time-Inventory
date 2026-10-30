using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;

namespace ManufacturingTimeTracking.Pages.Materials;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public IndexModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public IList<Models.Templates.Material> Materials { get; set; } = default!;

    /// <summary>Total stock quantity per material Id (for minimum-stock alert).</summary>
    public Dictionary<int, int> StockByMaterialId { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 10;
    public PaginationInfo Pagination { get; set; } = new();

    public async Task OnGetAsync()
    {
        var query = _context.Materials
            .Include(m => m.Supplier)
            .Include(m => m.Location)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim().ToLower();
            query = query.Where(m =>
                m.Name.ToLower().Contains(term) ||
                (m.Reference != null && m.Reference.ToLower().Contains(term)) ||
                (m.Qr != null && m.Qr.ToLower().Contains(term)) ||
                (m.Notes != null && m.Notes.ToLower().Contains(term)) ||
                (m.Supplier != null && m.Supplier.Name.ToLower().Contains(term)));
        }

        var ordered = query.OrderBy(m => m.Name);
        var totalItems = await ordered.CountAsync();
        Materials = await ordered.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToListAsync();

        var materialIds = Materials.Select(m => m.Id).ToList();
        var stockSums = await _context.MaterialStocks
            .Where(ms => materialIds.Contains(ms.MaterialId))
            .GroupBy(ms => ms.MaterialId)
            .Select(g => new { MaterialId = g.Key, Total = g.Sum(ms => ms.Quantity) })
            .ToListAsync();
        StockByMaterialId = stockSums.ToDictionary(x => x.MaterialId, x => x.Total);
        Pagination = new PaginationInfo { PageNumber = PageNumber, PageSize = PageSize, TotalItems = totalItems };
    }
}
