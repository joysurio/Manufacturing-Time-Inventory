using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.TimeTracking;

namespace ManufacturingTimeTracking.Pages.TimeTracking.BreakReasons;

[Authorize(Roles = "Admin")]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public EditModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public BreakReason? BreakReason { get; set; }

    [BindProperty]
    public EditBreakReasonInput Input { get; set; } = new();

    public class EditBreakReasonInput
    {
        [Required(ErrorMessage = "Name is required.")]
        [StringLength(200)]
        public string Name { get; set; } = "";
        [StringLength(500)]
        public string? Description { get; set; }
        public bool Active { get; set; } = true;
    }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null) return NotFound();
        BreakReason = await _context.BreakReasons.FindAsync(id);
        if (BreakReason == null) return NotFound();
        Input.Name = BreakReason.Name;
        Input.Description = BreakReason.Description;
        Input.Active = BreakReason.Active;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (id == null) return NotFound();
        var reason = await _context.BreakReasons.FindAsync(id);
        if (reason == null) return NotFound();
        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            BreakReason = reason;
            ModelState.AddModelError("Input.Name", "Name is required.");
            return Page();
        }
        reason.Name = Input.Name.Trim();
        reason.Description = Input.Description?.Trim();
        reason.Active = Input.Active;
        await _context.SaveChangesAsync();
        TempData["BreakReasonsMessage"] = "Break reason updated.";
        return RedirectToPage("./Index");
    }
}
