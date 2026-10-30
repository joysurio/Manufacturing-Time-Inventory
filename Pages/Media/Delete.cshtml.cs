using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using MediaModel = ManufacturingTimeTracking.Models.Templates.Media;

namespace ManufacturingTimeTracking.Pages.Media;

public class DeleteModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public DeleteModel(ApplicationDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public MediaModel Media { get; set; } = default!;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var media = await _context.Media.FirstOrDefaultAsync(m => m.Id == id);

        if (media == null)
        {
            return NotFound();
        }

        Media = media;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var media = await _context.Media.FindAsync(id);
        if (media != null)
        {
            _context.Media.Remove(media);
            await _context.SaveChangesAsync();
        }

        return RedirectToPage("./Index");
    }
}
