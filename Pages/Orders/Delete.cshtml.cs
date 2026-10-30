using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Execution;

namespace ManufacturingTimeTracking.Pages.Orders;

[Authorize(Roles = "Admin")]
public class DeleteModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public DeleteModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public BuildOrder? BuildOrder { get; set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null || _context.BuildOrders == null)
            return RedirectToPage("./Index");

        BuildOrder = await _context.BuildOrders.FindAsync(id);
        if (BuildOrder == null)
        {
            TempData["ToastrError"] = "Order not found.";
            return RedirectToPage("./Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (id == null || _context.BuildOrders == null)
        {
            TempData["ToastrError"] = "Order not found.";
            return RedirectToPage("./Index");
        }

        var order = await _context.BuildOrders.FindAsync(id);
        if (order == null)
        {
            TempData["ToastrError"] = "Order not found.";
            return RedirectToPage("./Index");
        }

        _context.BuildOrders.Remove(order);
        await _context.SaveChangesAsync();

        TempData["ToastrSuccess"] = "Order deleted successfully.";
        return RedirectToPage("./Index");
    }
}
