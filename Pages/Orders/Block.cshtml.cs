using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Execution;
using ManufacturingTimeTracking.Models.Catalog;

namespace ManufacturingTimeTracking.Pages.Orders;

[Authorize(Roles = "Admin,Supervisor")]
public class BlockModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public BlockModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public BuildOrder BuildOrder { get; set; } = default!;
    public SelectList BlockedReasons { get; set; } = default!;

    [BindProperty]
    public int BlockedReasonId { get; set; }

    [BindProperty]
    public string BlockedNotes { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null) return NotFound();

        var order = await _context.BuildOrders.FindAsync(id);
        if (order == null) return NotFound();

        if (order.IsBlocked)
        {
            TempData["ToastrWarning"] = "Order is already blocked.";
            return RedirectToPage("./Details", new { id });
        }

        BuildOrder = order;
        BlockedReasons = new SelectList(
            await _context.BlockedReasons.Where(r => r.Active).OrderBy(r => r.Name).ToListAsync(),
            "Id", "Name");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (id == null) return NotFound();

        var order = await _context.BuildOrders.FindAsync(id);
        if (order == null) return NotFound();

        if (order.IsBlocked)
        {
            TempData["ToastrWarning"] = "Order is already blocked.";
            return RedirectToPage("./Details", new { id });
        }

        if (BlockedReasonId <= 0)
        {
            ModelState.AddModelError("BlockedReasonId", "Please select a reason.");
            BuildOrder = order;
            BlockedReasons = new SelectList(
                await _context.BlockedReasons.Where(r => r.Active).OrderBy(r => r.Name).ToListAsync(),
                "Id", "Name");
            return Page();
        }
        if (string.IsNullOrWhiteSpace(BlockedNotes))
        {
            ModelState.AddModelError("BlockedNotes", "Notes are required when blocking an order.");
            BuildOrder = order;
            BlockedReasons = new SelectList(
                await _context.BlockedReasons.Where(r => r.Active).OrderBy(r => r.Name).ToListAsync(),
                "Id", "Name");
            return Page();
        }

        var reasonExists = await _context.BlockedReasons.AnyAsync(r => r.Id == BlockedReasonId && r.Active);
        if (!reasonExists)
        {
            ModelState.AddModelError("BlockedReasonId", "Please select a valid reason.");
            BuildOrder = order;
            BlockedReasons = new SelectList(
                await _context.BlockedReasons.Where(r => r.Active).OrderBy(r => r.Name).ToListAsync(),
                "Id", "Name");
            return Page();
        }

        order.IsBlocked = true;
        order.BlockedReasonId = BlockedReasonId;
        order.BlockedNotes = BlockedNotes.Trim();
        order.BlockedAt = DateTime.UtcNow;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        _context.ProductionInterruptions.Add(new ProductionInterruption
        {
            BuildOrderId = order.Id,
            Category = ProductionInterruptionCategory.Blocked,
            StartTime = DateTime.UtcNow,
            EndTime = null,
            Notes = BlockedNotes.Trim(),
            CreatedByUserId = userId
        });
        await _context.SaveChangesAsync();

        TempData["ToastrSuccess"] = "Order blocked successfully.";
        return RedirectToPage("./Details", new { id });
    }
}
