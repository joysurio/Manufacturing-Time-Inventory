using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Execution;

namespace ManufacturingTimeTracking.Pages.Orders;

[Authorize(Roles = "Admin,Supervisor")]
public class ReopenedStepsModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public ReopenedStepsModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public class ReopenedStepRow
    {
        public DateTime ReopenedAt { get; set; }
        public string StepTitle { get; set; } = "";
        public string OrderRef { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string UserEmail { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    public List<ReopenedStepRow> Rows { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var query = _context.ProductionInterruptions
            .Include(pi => pi.BuildOrder)
            .Include(pi => pi.StepExec)
            .Where(pi => pi.Category == ProductionInterruptionCategory.Reopened)
            .OrderByDescending(pi => pi.StartTime)
            .Take(500);

        var userIds = await query
            .Select(pi => pi.CreatedByUserId)
            .Distinct()
            .ToListAsync();

        var userMap = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName ?? u.Id);

        Rows = await query
            .Select(pi => new ReopenedStepRow
            {
                ReopenedAt = pi.StartTime,
                StepTitle = pi.StepExec != null ? pi.StepExec.Title : "(unknown step)",
                OrderRef = pi.BuildOrder.ExternalRef ?? "",
                SerialNumber = pi.BuildOrder.SerialNumber ?? "",
                UserEmail = userMap.ContainsKey(pi.CreatedByUserId) ? userMap[pi.CreatedByUserId] : (pi.CreatedByUserId ?? ""),
                Reason = pi.Notes
            })
            .ToListAsync();

        return Page();
    }
}

