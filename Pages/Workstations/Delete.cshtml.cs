using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Catalog;

namespace ManufacturingTimeTracking.Pages.Workstations;

public class DeleteModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public DeleteModel(ApplicationDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public Workstation Workstation { get; set; } = default!;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var workstation = await _context.Workstations.FirstOrDefaultAsync(w => w.Id == id);

        if (workstation == null)
        {
            return NotFound();
        }

        Workstation = workstation;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var workstation = await _context.Workstations.FindAsync(id);
        if (workstation != null)
        {
            _context.Workstations.Remove(workstation);
            await _context.SaveChangesAsync();
        }

        return RedirectToPage("./Index");
    }
}
