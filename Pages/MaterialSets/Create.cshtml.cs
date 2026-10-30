using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Inventory;
using ManufacturingTimeTracking.Models.Templates;

namespace ManufacturingTimeTracking.Pages.MaterialSets;

public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public CreateModel(ApplicationDbContext context) => _context = context;

    [BindProperty]
    [Required, MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    [MaxLength(512)]
    public string? Description { get; set; }

    [BindProperty]
    public List<MaterialSetItemInput> Items { get; set; } = new() { new MaterialSetItemInput() };

    public SelectList MaterialsSelectList { get; set; } = null!;
    /// <summary>For JavaScript: list of { value, text } for material dropdown.</summary>
    public List<(string Value, string Text)> MaterialsForScript { get; set; } = new();

    public async Task OnGetAsync()
    {
        await LoadMaterialsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var validItems = Items?.Where(i => i.MaterialId > 0 && i.Quantity > 0).ToList() ?? new List<MaterialSetItemInput>();
        if (validItems.Count == 0)
            ModelState.AddModelError("", "Add at least one material with quantity greater than 0.");

        if (!ModelState.IsValid)
        {
            await LoadMaterialsAsync();
            return Page();
        }

        var set = new MaterialSet
        {
            Name = Name.Trim(),
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim()
        };
        _context.MaterialSets.Add(set);
        await _context.SaveChangesAsync();

        for (var i = 0; i < validItems.Count; i++)
        {
            _context.MaterialSetItems.Add(new MaterialSetItem
            {
                MaterialSetId = set.Id,
                MaterialId = validItems[i].MaterialId,
                Quantity = validItems[i].Quantity,
                SortOrder = i
            });
        }
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Material set \"{set.Name}\" created.";
        return RedirectToPage("./Index");
    }

    private async Task LoadMaterialsAsync()
    {
        var materials = await _context.Materials.OrderBy(m => m.Name).ToListAsync();
        MaterialsSelectList = new SelectList(materials, "Id", "Name");
        MaterialsForScript = materials
            .Select(m =>
            {
                var label = string.IsNullOrWhiteSpace(m.Reference)
                    ? m.Name
                    : $"{m.Name} - {m.Reference}";
                return (m.Id.ToString(), label);
            })
            .ToList();
    }
}

public class MaterialSetItemInput
{
    public int MaterialId { get; set; }
    public int Quantity { get; set; } = 1;
}
