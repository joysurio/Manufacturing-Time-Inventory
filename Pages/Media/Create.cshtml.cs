using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ManufacturingTimeTracking.Data;
using MediaModel = ManufacturingTimeTracking.Models.Templates.Media;

namespace ManufacturingTimeTracking.Pages.Media;

public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public CreateModel(ApplicationDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public MediaModel Media { get; set; } = default!;

    public SelectList MediaTypes { get; set; } = default!;

    public IActionResult OnGet()
    {
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

        Media.UploadedBy = User.Identity?.Name ?? "System";
        Media.UploadedAt = DateTime.UtcNow;

        _context.Media.Add(Media);
        await _context.SaveChangesAsync();

        return RedirectToPage("./Index");
    }
}
