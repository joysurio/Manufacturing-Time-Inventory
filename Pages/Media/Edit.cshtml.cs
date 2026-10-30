using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using MediaModel = ManufacturingTimeTracking.Models.Templates.Media;

namespace ManufacturingTimeTracking.Pages.Media;

public class EditModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public EditModel(ApplicationDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public MediaModel Media { get; set; } = default!;

    public SelectList MediaTypes { get; set; } = default!;

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
        MediaTypes = new SelectList(new[] { "Image", "Video" });
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            MediaTypes = new SelectList(new[] { "Image", "Video" });
            return Page();
        }

        _context.Attach(Media).State = EntityState.Modified;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!MediaExists(Media.Id))
            {
                return NotFound();
            }
            else
            {
                throw;
            }
        }

        return RedirectToPage("./Index");
    }

    private bool MediaExists(int id)
    {
        return _context.Media.Any(e => e.Id == id);
    }
}
