using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Templates;

namespace ManufacturingTimeTracking.Pages.Materials;

public class PrintQrModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public PrintQrModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public Material? Material { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Material = await _context.Materials.FindAsync(id);
        return Page();
    }
}
