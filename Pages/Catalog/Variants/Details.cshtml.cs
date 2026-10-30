using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Catalog;

namespace ManufacturingTimeTracking.Pages.Catalog.Variants;

public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public DetailsModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public MachineVariant Variant { get; set; } = default!;

    /// <summary>JSON array of { Id, Name } for the material combobox filter.</summary>
    public string MaterialListJson { get; set; } = "[]";

    [BindProperty]
    public int? NewComponentMaterialId { get; set; }

    [BindProperty]
    public int NewComponentQuantity { get; set; } = 1;

    [BindProperty]
    public string? NewComponentNotes { get; set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null) return NotFound();

        var variant = await _context.MachineVariants
            .Include(v => v.MachineModel)
            .Include(v => v.Components)
                .ThenInclude(c => c.Material)
            .Include(v => v.Components)
                .ThenInclude(c => c.Item)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (variant == null) return NotFound();

        Variant = variant;

        var materials = await _context.Materials.OrderBy(m => m.Name).ToListAsync();
        MaterialListJson = JsonSerializer.Serialize(materials.Select(m => new { m.Id, m.Name }));

        return Page();
    }

    public async Task<IActionResult> OnPostAddComponentAsync(int? id)
    {
        if (id == null || id <= 0) return NotFound();

        if (!NewComponentMaterialId.HasValue || NewComponentMaterialId.Value <= 0 || NewComponentQuantity < 1)
        {
            TempData["Error"] = "Select a material and enter quantity (≥ 1).";
            return RedirectToPage("./Details", new { id });
        }

        var variant = await _context.MachineVariants
            .Include(v => v.MachineModel)
            .FirstOrDefaultAsync(v => v.Id == id);
        if (variant == null) return NotFound();

        var exists = await _context.MachineVariantComponents
            .AnyAsync(c => c.MachineVariantId == id && c.MaterialId == NewComponentMaterialId.Value);
        if (exists)
        {
            TempData["Error"] = "This material is already assigned to this variant.";
            return RedirectToPage("./Details", new { id });
        }

        _context.MachineVariantComponents.Add(new MachineVariantComponent
        {
            MachineVariantId = id.Value,
            MaterialId = NewComponentMaterialId.Value,
            Quantity = NewComponentQuantity,
            Notes = string.IsNullOrWhiteSpace(NewComponentNotes) ? null : NewComponentNotes.Trim()
        });
        await _context.SaveChangesAsync();

        TempData["Success"] = "Variant material added.";
        return RedirectToPage("./Details", new { id });
    }

    public async Task<IActionResult> OnPostDeleteComponentAsync(int componentId, int id)
    {
        var comp = await _context.MachineVariantComponents
            .FirstOrDefaultAsync(c => c.Id == componentId && c.MachineVariantId == id);
        if (comp != null)
        {
            _context.MachineVariantComponents.Remove(comp);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Variant material removed.";
        }
        return RedirectToPage("./Details", new { id });
    }
}

