using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Catalog;
using ManufacturingTimeTracking.Models.Templates;

namespace ManufacturingTimeTracking.Pages.Catalog.Models;

public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public DetailsModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public MachineModel MachineModel { get; set; } = default!;

    [BindProperty]
    public MachineVariant NewVariant { get; set; } = new();

    public SelectList ItemSelectList { get; set; } = null!;
    public SelectList MaterialSelectList { get; set; } = null!;

    /// <summary>JSON array of { Id, Name } for the material combobox filter.</summary>
    public string MaterialListJson { get; set; } = "[]";

    [BindProperty]
    public int? NewComponentMaterialId { get; set; }

    [BindProperty]
    public int? NewComponentItemId { get; set; }

    [BindProperty]
    public int NewComponentQuantity { get; set; } = 1;

    [BindProperty]
    public string? NewComponentNotes { get; set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var machineModel = await _context.MachineModels
            .Include(m => m.Variants)
            .Include(m => m.Components)
                .ThenInclude(c => c.Item)
            .Include(m => m.Components)
                .ThenInclude(c => c.Material)
            .Include(m => m.Components)
                .ThenInclude(c => c.Alternatives)
                    .ThenInclude(a => a.Item)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (machineModel == null)
        {
            return NotFound();
        }

        MachineModel = machineModel;
        NewVariant.MachineModelId = machineModel.Id;
        var items = await _context.Items.OrderBy(i => i.Sku).ToListAsync();
        ItemSelectList = new SelectList(items, "Id", "Sku", null, "ModelOrType");
        var materials = await _context.Materials.OrderBy(m => m.Name).ToListAsync();
        MaterialSelectList = new SelectList(materials, "Id", "Name");
        MaterialListJson = JsonSerializer.Serialize(materials.Select(m => new { m.Id, m.Name }));
        return Page();
    }

    public async Task<IActionResult> OnPostAddVariantAsync(int? id)
    {
        // Get the model ID from route or form
        var modelId = id ?? NewVariant.MachineModelId;
        
        if (modelId <= 0)
        {
            ModelState.AddModelError("", "Invalid model ID. Please try again.");
            MachineModel = await _context.MachineModels
                .Include(m => m.Variants)
                .FirstOrDefaultAsync(m => m.Id == modelId) ?? default!;
            return Page();
        }

        // Reload the model for display
        MachineModel = await _context.MachineModels
            .Include(m => m.Variants)
            .FirstOrDefaultAsync(m => m.Id == modelId) ?? default!;

        if (MachineModel == null)
        {
            ModelState.AddModelError("", "Machine model not found.");
            return Page();
        }

        // Ensure MachineModelId is set
        NewVariant.MachineModelId = modelId;

        // Validate required fields
        if (string.IsNullOrWhiteSpace(NewVariant.Name))
        {
            ModelState.AddModelError("NewVariant.Name", "Name is required.");
        }

        if (string.IsNullOrWhiteSpace(NewVariant.Code))
        {
            ModelState.AddModelError("NewVariant.Code", "Code is required.");
        }

        // Remove validation errors for navigation property
        ModelState.Remove("NewVariant.MachineModel");

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            NewVariant.Active = true;
            // Don't set the navigation property, just the ID
            _context.MachineVariants.Add(NewVariant);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Variant '{NewVariant.Name}' added successfully!";
            return RedirectToPage("./Details", new { id = modelId });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"Error adding variant: {ex.Message}");
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteVariantAsync(int variantId, int modelId)
    {
        var variant = await _context.MachineVariants.FindAsync(variantId);
        if (variant != null)
        {
            _context.MachineVariants.Remove(variant);
            await _context.SaveChangesAsync();
        }

        return RedirectToPage("./Details", new { id = modelId });
    }

    public async Task<IActionResult> OnPostUpdateVariantAsync(int variantId, int modelId, string name, string code)
    {
        var variant = await _context.MachineVariants.FindAsync(variantId);
        if (variant == null)
        {
            TempData["Error"] = "Variant not found.";
            return RedirectToPage("./Details", new { id = modelId });
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            TempData["Error"] = "Name and Code are required.";
            return RedirectToPage("./Details", new { id = modelId });
        }

        variant.Name = name.Trim();
        variant.Code = code.Trim();
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Variant '{variant.Name}' updated successfully!";
        return RedirectToPage("./Details", new { id = modelId });
    }

    public async Task<IActionResult> OnPostAddComponentAsync(int? id, int? newComponentMaterialId, int? newComponentItemId, int newComponentQuantity, string? newComponentNotes)
    {
        var modelId = id ?? 0;
        if (modelId <= 0 || newComponentQuantity < 1)
        {
            TempData["Error"] = "Select a material and enter quantity (≥ 1).";
            return RedirectToPage("./Details", new { id = modelId });
        }
        if (newComponentMaterialId.HasValue && newComponentMaterialId.Value > 0)
        {
            var exists = await _context.MachineModelComponents
                .AnyAsync(c => c.MachineModelId == modelId && c.MaterialId == newComponentMaterialId.Value);
            if (exists)
            {
                TempData["Error"] = "This material is already in the component list.";
                return RedirectToPage("./Details", new { id = modelId });
            }
            _context.MachineModelComponents.Add(new MachineModelComponent
            {
                MachineModelId = modelId,
                MaterialId = newComponentMaterialId.Value,
                Quantity = newComponentQuantity,
                Notes = string.IsNullOrWhiteSpace(newComponentNotes) ? null : newComponentNotes.Trim()
            });
        }
        else if (newComponentItemId.HasValue && newComponentItemId.Value > 0)
        {
            var exists = await _context.MachineModelComponents
                .AnyAsync(c => c.MachineModelId == modelId && c.ItemId == newComponentItemId.Value);
            if (exists)
            {
                TempData["Error"] = "This item is already in the component list.";
                return RedirectToPage("./Details", new { id = modelId });
            }
            _context.MachineModelComponents.Add(new MachineModelComponent
            {
                MachineModelId = modelId,
                ItemId = newComponentItemId.Value,
                Quantity = newComponentQuantity,
                Notes = string.IsNullOrWhiteSpace(newComponentNotes) ? null : newComponentNotes.Trim()
            });
        }
        else
        {
            TempData["Error"] = "Select a material and enter quantity (≥ 1).";
            return RedirectToPage("./Details", new { id = modelId });
        }
        await _context.SaveChangesAsync();
        TempData["Success"] = "Component added. Use Orders to generate pick lists.";
        return RedirectToPage("./Details", new { id = modelId });
    }

    public async Task<IActionResult> OnPostDeleteComponentAsync(int componentId, int modelId)
    {
        var comp = await _context.MachineModelComponents.FindAsync(componentId);
        if (comp != null)
        {
            _context.MachineModelComponents.Remove(comp);
            await _context.SaveChangesAsync();
        }
        return RedirectToPage("./Details", new { id = modelId });
    }

    public async Task<IActionResult> OnPostAddAlternativeAsync(int componentId, int modelId, int itemId)
    {
        if (itemId <= 0)
        {
            TempData["Error"] = "Select an item.";
            return RedirectToPage("./Details", new { id = modelId });
        }

        var component = await _context.MachineModelComponents
            .Include(c => c.Alternatives)
            .FirstOrDefaultAsync(c => c.Id == componentId);
        
        if (component == null)
        {
            TempData["Error"] = "Component not found.";
            return RedirectToPage("./Details", new { id = modelId });
        }

        // Check if item is already the primary item or already an alternative
        if (component.ItemId == itemId || component.Alternatives.Any(a => a.ItemId == itemId))
        {
            TempData["Error"] = "This item is already the primary item or already added as an alternative.";
            return RedirectToPage("./Details", new { id = modelId });
        }

        var maxSort = component.Alternatives.Any() ? component.Alternatives.Max(a => a.SortOrder) : -1;
        _context.MachineModelComponentAlternatives.Add(new MachineModelComponentAlternative
        {
            ComponentId = componentId,
            ItemId = itemId,
            SortOrder = maxSort + 1
        });
        await _context.SaveChangesAsync();
        TempData["Success"] = "Alternative added successfully.";
        return RedirectToPage("./Details", new { id = modelId });
    }

    public async Task<IActionResult> OnPostDeleteAlternativeAsync(int alternativeId, int modelId)
    {
        var alt = await _context.MachineModelComponentAlternatives.FindAsync(alternativeId);
        if (alt != null)
        {
            _context.MachineModelComponentAlternatives.Remove(alt);
            await _context.SaveChangesAsync();
        }
        return RedirectToPage("./Details", new { id = modelId });
    }
}
