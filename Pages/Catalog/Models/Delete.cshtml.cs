using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Catalog;

namespace ManufacturingTimeTracking.Pages.Catalog.Models;

public class DeleteModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public DeleteModel(ApplicationDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public MachineModel MachineModel { get; set; } = default!;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var machineModel = await _context.MachineModels
            .Include(m => m.Variants)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (machineModel == null)
        {
            return NotFound();
        }

        MachineModel = machineModel;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var machineModel = await _context.MachineModels
            .Include(m => m.Variants)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (machineModel != null)
        {
            // Delete all variants first
            _context.MachineVariants.RemoveRange(machineModel.Variants);
            
            // Then delete the model
            _context.MachineModels.Remove(machineModel);
            await _context.SaveChangesAsync();
        }

        return RedirectToPage("./Index");
    }
}
