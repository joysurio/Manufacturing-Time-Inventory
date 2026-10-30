using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Models.Execution;
using ManufacturingTimeTracking.Models.Catalog;

namespace ManufacturingTimeTracking.Pages.Orders;

/// <summary>Weekly load view: planned StandardHours vs capacity (issue #14).</summary>
[Authorize]
public class WeeklyLoadModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public WeeklyLoadModel(ApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    public decimal WeeklyCapacityHours { get; set; }
    public List<WeeklyLoadRow> Rows { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 10;
    public PaginationInfo Pagination { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        WeeklyCapacityHours = _configuration.GetValue<decimal>("Planning:WeeklyCapacityHours", 40m);
        if (WeeklyCapacityHours <= 0) WeeklyCapacityHours = 40m;

        var ordersWithWeek = await _context.BuildOrders
            .AsNoTracking()
            .Where(o => o.PlannedWeek != null && o.PlannedWeek != "" && o.Status != "Cancelled")
            .Include(o => o.Executions)
            .Select(o => new { o.Id, o.PlannedWeek, o.MachineModelId })
            .ToListAsync();

        var modelIds = ordersWithWeek.Select(o => o.MachineModelId).Distinct().ToList();
        var models = await _context.MachineModels
            .AsNoTracking()
            .Where(m => modelIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.StandardHours);

        var byWeek = ordersWithWeek
            .GroupBy(o => o.PlannedWeek)
            .Select(g => new WeeklyLoadRow
            {
                PlannedWeek = g.Key!,
                PlannedHours = g.Sum(o => models.GetValueOrDefault(o.MachineModelId, 0)),
                CapacityHours = WeeklyCapacityHours,
                UtilizationPercent = WeeklyCapacityHours > 0
                    ? (g.Sum(o => models.GetValueOrDefault(o.MachineModelId, 0)) / WeeklyCapacityHours) * 100
                    : 0,
                IsOverload = g.Sum(o => models.GetValueOrDefault(o.MachineModelId, 0)) > WeeklyCapacityHours,
                OrderCount = g.Count()
            })
            .OrderBy(r => r.PlannedWeek)
            .ToList();

        var totalItems = byWeek.Count;
        Rows = byWeek.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
        Pagination = new PaginationInfo { PageNumber = PageNumber, PageSize = PageSize, TotalItems = totalItems };
        return Page();
    }
}

public class WeeklyLoadRow
{
    public string PlannedWeek { get; set; } = "";
    public decimal PlannedHours { get; set; }
    public decimal CapacityHours { get; set; }
    public decimal UtilizationPercent { get; set; }
    public bool IsOverload { get; set; }
    public int OrderCount { get; set; }
}
