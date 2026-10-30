using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Catalog;

namespace ManufacturingTimeTracking.Pages.Catalog.Models;

public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public CreateModel(ApplicationDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public MachineModel MachineModel { get; set; } = default!;

    public IActionResult OnGet()
    {
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid || _context.MachineModels == null || MachineModel == null)
        {
            return Page();
        }

        MachineModel.Active = true;
        _context.MachineModels.Add(MachineModel);
        await _context.SaveChangesAsync();

        return RedirectToPage("./Index");
    }
}
