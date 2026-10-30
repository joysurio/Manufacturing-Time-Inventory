using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Models.Execution;
using ManufacturingTimeTracking.Models.Catalog;

namespace ManufacturingTimeTracking.Pages.Orders;

/// <summary>Variance report by model: StandardHours vs avg RealHours (issue #15).</summary>
[Authorize]
public class VarianceReportModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public VarianceReportModel(ApplicationDbContext context)
    {
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 10;
    public PaginationInfo Pagination { get; set; } = new();

    public List<VarianceReportRow> Rows { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var from = FromDate?.Date ?? DateTime.UtcNow.Date.AddMonths(-3);
        var to = ToDate?.Date ?? DateTime.UtcNow.Date;
        FromDate = from;
        ToDate = to;
        var fromUtc = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to.AddDays(1), DateTimeKind.Utc);

        var orderIdsWithActivityInRange = await _context.StepRuns
            .AsNoTracking()
            .Where(r => r.StartedAt >= fromUtc && r.StartedAt < toUtc)
            .Select(r => r.StepExec!.PhaseExec!.BuildExecution!.BuildOrderId)
            .Distinct()
            .ToListAsync();

        if (orderIdsWithActivityInRange.Count == 0)
        {
            Rows = new List<VarianceReportRow>();
            return Page();
        }

        var orders = await _context.BuildOrders
            .AsNoTracking()
            .Where(o => orderIdsWithActivityInRange.Contains(o.Id))
            .Select(o => new { o.Id, o.MachineModelId })
            .ToListAsync();

        var runsWithOrderId = await _context.BuildExecutions
            .AsNoTracking()
            .Where(be => orderIdsWithActivityInRange.Contains(be.BuildOrderId))
            .SelectMany(be => be.Phases.SelectMany(p => p.Steps.SelectMany(s => s.Runs
                .Where(r => r.DurationSeconds != null)
                .Select(r => new { be.BuildOrderId, r.DurationSeconds }))))
            .ToListAsync();

        var realHoursPerOrder = runsWithOrderId
            .GroupBy(x => x.BuildOrderId)
            .ToDictionary(g => g.Key, g => g.Sum(x => (x.DurationSeconds ?? 0) / 3600m));

        var modelIds = orders.Select(o => o.MachineModelId).Distinct().ToList();
        var models = await _context.MachineModels
            .AsNoTracking()
            .Where(m => modelIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => (Name: m.Name, StandardHours: m.StandardHours));

        var byModel = orders
            .Where(o => realHoursPerOrder.TryGetValue(o.Id, out var h) && h > 0)
            .GroupBy(o => o.MachineModelId)
            .Select(g =>
            {
                var list = g.ToList();
                var orderIds = list.Select(x => x.Id).ToList();
                var realHoursList = orderIds.Select(id => realHoursPerOrder[id]).ToList();
                var avgReal = realHoursList.Average();
                var modelInfo = models.TryGetValue(g.Key, out var info) ? info : (Name: $"Model {g.Key}", StandardHours: 0m);
                var standard = modelInfo.StandardHours;
                var variancePct = standard > 0 ? ((avgReal - standard) / standard) * 100 : 0m;
                return new VarianceReportRow
                {
                    ModelId = g.Key,
                    ModelName = modelInfo.Name,
                    AvgRealHours = avgReal,
                    StandardHours = standard,
                    VariancePercent = variancePct,
                    SampleSize = realHoursList.Count
                };
            })
            .OrderBy(r => r.ModelName)
            .ToList();

        var totalItems = byModel.Count;
        Rows = byModel.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
        Pagination = new PaginationInfo { PageNumber = PageNumber, PageSize = PageSize, TotalItems = totalItems };
        return Page();
    }
}

public class VarianceReportRow
{
    public int ModelId { get; set; }
    public string ModelName { get; set; } = "";
    public decimal AvgRealHours { get; set; }
    public decimal StandardHours { get; set; }
    public decimal VariancePercent { get; set; }
    public int SampleSize { get; set; }
}
