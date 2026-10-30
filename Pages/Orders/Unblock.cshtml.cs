using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Execution;

namespace ManufacturingTimeTracking.Pages.Orders;

[Authorize(Roles = "Admin,Supervisor")]
public class UnblockModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public UnblockModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public int OrderId { get; set; }
    public string ExternalRef { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;

    [BindProperty]
    public string? UnblockNote { get; set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null) return NotFound();

        var order = await _context.BuildOrders.FindAsync(id);
        if (order == null) return NotFound();

        if (!order.IsBlocked)
        {
            TempData["ToastrWarning"] = "Order is not blocked.";
            return RedirectToPage("./Details", new { id });
        }

        OrderId = order.Id;
        ExternalRef = order.ExternalRef;
        SerialNumber = order.SerialNumber;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (id == null) return NotFound();

        var order = await _context.BuildOrders.FindAsync(id);
        if (order == null) return NotFound();

        if (!order.IsBlocked)
        {
            TempData["ToastrWarning"] = "Order is not blocked.";
            return RedirectToPage("./Details", new { id });
        }

        order.IsBlocked = false;
        order.BlockedReasonId = null;
        order.BlockedNotes = null;
        order.BlockedAt = null;

        var activeBlocked = await _context.ProductionInterruptions
            .Where(pi => pi.BuildOrderId == order.Id && pi.Category == ProductionInterruptionCategory.Blocked && pi.EndTime == null)
            .ToListAsync();
        foreach (var pi in activeBlocked)
        {
            pi.EndTime = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync();

        TempData["ToastrSuccess"] = "Order unblocked successfully.";
        return RedirectToPage("./Details", new { id });
    }
}
