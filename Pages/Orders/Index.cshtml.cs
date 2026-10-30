using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Models.Execution;
using ManufacturingTimeTracking.Models.Catalog;

namespace ManufacturingTimeTracking.Pages.Orders;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public IndexModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public IList<BuildOrder> BuildOrders { get; set; } = default!;
    public Dictionary<int, string> ModelNames { get; set; } = new();
    public Dictionary<int, string> VariantNames { get; set; } = new();

    /// <summary>Filter: "Active" (default), "Completed", or "All"</summary>
    [BindProperty(SupportsGet = true)]
    public string StatusFilter { get; set; } = "Active";
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 10;

    // Column filters (server-side). These must work across pages.
    [BindProperty(SupportsGet = true)] public string? ExternalRefFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? SerialNumberFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? ModelFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? VariantFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? ClientFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? StatusTextFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? CreatedFilter { get; set; }
    public PaginationInfo Pagination { get; set; } = new();

    public async Task OnGetAsync()
    {
        var query = _context.BuildOrders
            .Include(o => o.BlockedReason)
            .Include(o => o.Executions)
            .AsQueryable();

        if (StatusFilter == "Active")
            query = query.Where(o => o.Status != "Completed" && o.Status != "Cancelled");
        else if (StatusFilter == "Completed")
            query = query.Where(o => o.Status == "Completed");

        query = query.OrderByDescending(o => o.CreatedAt);

        // Server-side filters must be applied BEFORE paging, otherwise they only affect one page.
        if (!string.IsNullOrWhiteSpace(ExternalRefFilter))
            query = query.Where(o => (o.ExternalRef ?? "").Contains(ExternalRefFilter));
        if (!string.IsNullOrWhiteSpace(SerialNumberFilter))
            query = query.Where(o => (o.SerialNumber ?? "").Contains(SerialNumberFilter));
        if (!string.IsNullOrWhiteSpace(ClientFilter))
            query = query.Where(o => (o.CustomerName ?? "").Contains(ClientFilter));
        if (!string.IsNullOrWhiteSpace(StatusTextFilter))
            query = query.Where(o => (o.Status ?? "").Contains(StatusTextFilter));

        // Model/Variant names are in separate tables; filter by matching IDs.
        if (!string.IsNullOrWhiteSpace(ModelFilter))
        {
            var modelIdsForFilter = await _context.MachineModels
                .Where(m => m.Name.Contains(ModelFilter))
                .Select(m => m.Id)
                .ToListAsync();
            if (modelIdsForFilter.Count == 0) { BuildOrders = new List<BuildOrder>(); Pagination = new PaginationInfo { PageNumber = PageNumber, PageSize = PageSize, TotalItems = 0 }; return; }
            query = query.Where(o => modelIdsForFilter.Contains(o.MachineModelId));
        }
        if (!string.IsNullOrWhiteSpace(VariantFilter))
        {
            var variantIdsForFilter = await _context.MachineVariants
                .Where(v => v.Name.Contains(VariantFilter))
                .Select(v => v.Id)
                .ToListAsync();
            if (variantIdsForFilter.Count == 0) { BuildOrders = new List<BuildOrder>(); Pagination = new PaginationInfo { PageNumber = PageNumber, PageSize = PageSize, TotalItems = 0 }; return; }
            query = query.Where(o => variantIdsForFilter.Contains(o.MachineVariantId));
        }

        // Created filter: allow quick text match on date/time string (local) by applying a coarse UTC window when it looks like yyyy-MM-dd.
        if (!string.IsNullOrWhiteSpace(CreatedFilter))
        {
            var f = CreatedFilter.Trim();
            if (DateTime.TryParse(f, out var dt))
            {
                // If user typed a date (no time), match that whole day in local time.
                var localStart = dt.Date;
                var localEnd = localStart.AddDays(1);
                var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, TimeZoneInfo.Local);
                var endUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, TimeZoneInfo.Local);
                query = query.Where(o => o.CreatedAt >= startUtc && o.CreatedAt < endUtc);
            }
        }

        var totalItems = await query.CountAsync();
        BuildOrders = await query.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToListAsync();
        Pagination = new PaginationInfo { PageNumber = PageNumber, PageSize = PageSize, TotalItems = totalItems };

        var modelIds = BuildOrders.Select(o => o.MachineModelId).Distinct().ToList();
        var variantIds = BuildOrders.Select(o => o.MachineVariantId).Distinct().ToList();

        if (modelIds.Any())
        {
            var models = await _context.MachineModels.Where(m => modelIds.Contains(m.Id)).ToListAsync();
            ModelNames = models.ToDictionary(m => m.Id, m => m.Name);
        }
        if (variantIds.Any())
        {
            var variants = await _context.MachineVariants.Where(v => variantIds.Contains(v.Id)).ToListAsync();
            VariantNames = variants.ToDictionary(v => v.Id, v => v.Name);
        }
    }

    /// <summary>Mark an order as completed.</summary>
    public async Task<IActionResult> OnPostCompleteOrderAsync(int? id)
    {
        if (id == null) return RedirectToPage();
        var order = await _context.BuildOrders.FindAsync(id);
        if (order == null) return NotFound();
        if (order.Status != "Pending" && order.Status != "InProgress")
        {
            TempData["ToastrWarning"] = "Order cannot be completed (it may already be completed or cancelled).";
            return RedirectToPage(new { StatusFilter, PageNumber, PageSize });
        }
        order.Status = "Completed";
        await _context.SaveChangesAsync();
        TempData["ToastrSuccess"] = $"Order {order.SerialNumber} marked as completed.";
        return RedirectToPage(new { StatusFilter, PageNumber, PageSize });
    }

    /// <summary>Reopen a completed order (back to Pending).</summary>
    public async Task<IActionResult> OnPostReopenOrderAsync(int? id)
    {
        if (id == null) return RedirectToPage();
        var order = await _context.BuildOrders.FindAsync(id);
        if (order == null) return NotFound();
        if (order.Status != "Completed")
        {
            TempData["ToastrWarning"] = "Only completed orders can be reopened.";
            return RedirectToPage(new { StatusFilter, PageNumber, PageSize });
        }

        order.Status = "Pending";
        await _context.SaveChangesAsync();
        TempData["ToastrSuccess"] = $"Order {order.SerialNumber} reopened as pending.";
        return RedirectToPage(new { StatusFilter, PageNumber, PageSize });
    }
}
