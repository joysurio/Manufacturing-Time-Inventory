using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Templates;

namespace ManufacturingTimeTracking.Pages.Tools;

public class DeleteModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public DeleteModel(ApplicationDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public Tool Tool { get; set; } = default!;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var tool = await _context.Tools.FirstOrDefaultAsync(m => m.Id == id);

        if (tool == null)
        {
            return NotFound();
        }

        Tool = tool;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var tool = await _context.Tools.FindAsync(id);
        if (tool != null)
        {
            _context.Tools.Remove(tool);
            await _context.SaveChangesAsync();
        }

        return RedirectToPage("./Index");
    }
}
