using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Models.Execution;

namespace ManufacturingTimeTracking.Pages.Orders;

[Authorize]
public class BlockagesModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public BlockagesModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public List<BlockageRow> Rows { get; set; } = new();
    public int ActiveCount { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 10;
    public PaginationInfo Pagination { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var query = _context.ProductionInterruptions
            .AsNoTracking()
            .Where(pi => pi.Category == ProductionInterruptionCategory.Blocked)
            .Include(pi => pi.BuildOrder)
                .ThenInclude(o => o.BlockedReason)
            .OrderByDescending(pi => pi.StartTime);

        ActiveCount = await _context.ProductionInterruptions
            .Where(pi => pi.Category == ProductionInterruptionCategory.Blocked && pi.EndTime == null)
            .CountAsync();

        var totalItems = await query.CountAsync();
        var interruptions = await query.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToListAsync();
        Pagination = new PaginationInfo { PageNumber = PageNumber, PageSize = PageSize, TotalItems = totalItems };

        Rows = interruptions.Select(pi => new BlockageRow
        {
            Id = pi.Id,
            OrderId = pi.BuildOrderId,
            ExternalRef = pi.BuildOrder?.ExternalRef ?? "",
            SerialNumber = pi.BuildOrder?.SerialNumber ?? "",
            ReasonName = pi.BuildOrder?.BlockedReason?.Name ?? "—",
            Notes = pi.Notes,
            StartTime = pi.StartTime,
            EndTime = pi.EndTime,
            IsActive = pi.EndTime == null,
            CreatedBy = pi.CreatedByUserId
        }).ToList();

        return Page();
    }
}

public class BlockageRow
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string ExternalRef { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string ReasonName { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public bool IsActive { get; set; }
    public string CreatedBy { get; set; } = "";
}
