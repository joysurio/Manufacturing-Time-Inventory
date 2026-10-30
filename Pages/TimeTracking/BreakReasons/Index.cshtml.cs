using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.TimeTracking;

namespace ManufacturingTimeTracking.Pages.TimeTracking.BreakReasons;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public IndexModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public List<BreakReason> BreakReasons { get; set; } = new();
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }

    [BindProperty]
    public CreateBreakReasonInput CreateInput { get; set; } = new();

    public class CreateBreakReasonInput
    {
        [Required(ErrorMessage = "Name is required.")]
        [StringLength(200)]
        public string Name { get; set; } = "";
        [StringLength(500)]
        public string? Description { get; set; }
        public bool Active { get; set; } = true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        BreakReasons = await _context.BreakReasons
            .OrderBy(r => r.Name)
            .ToListAsync();
        if (TempData["BreakReasonsMessage"] != null)
            Message = TempData["BreakReasonsMessage"]?.ToString();
        if (TempData["BreakReasonsError"] != null)
            ErrorMessage = TempData["BreakReasonsError"]?.ToString();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(CreateInput.Name))
        {
            TempData["BreakReasonsError"] = "Name is required.";
            return RedirectToPage();
        }
        _context.BreakReasons.Add(new BreakReason
        {
            Name = CreateInput.Name.Trim(),
            Description = CreateInput.Description?.Trim(),
            Active = CreateInput.Active
        });
        await _context.SaveChangesAsync();
        TempData["BreakReasonsMessage"] = "Break reason added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        var reason = await _context.BreakReasons.FindAsync(id);
        if (reason == null)
        {
            TempData["BreakReasonsError"] = "Reason not found.";
            return RedirectToPage();
        }
        reason.Active = !reason.Active;
        await _context.SaveChangesAsync();
        TempData["BreakReasonsMessage"] = reason.Active ? "Reason enabled." : "Reason disabled.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var reason = await _context.BreakReasons.FindAsync(id);
        if (reason == null)
        {
            TempData["BreakReasonsError"] = "Reason not found.";
            return RedirectToPage();
        }
        var used = await _context.TimeTrackingEntries.AnyAsync(e => e.BreakReasonId == id);
        if (used)
        {
            TempData["BreakReasonsError"] = "Cannot delete: this reason is used in time tracking records. Disable it instead.";
            return RedirectToPage();
        }
        _context.BreakReasons.Remove(reason);
        await _context.SaveChangesAsync();
        TempData["BreakReasonsMessage"] = "Break reason deleted.";
        return RedirectToPage();
    }
}
