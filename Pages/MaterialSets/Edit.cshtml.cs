using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Inventory;

namespace ManufacturingTimeTracking.Pages.MaterialSets;

public class EditModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public EditModel(ApplicationDbContext context) => _context = context;

    public MaterialSet? MaterialSet { get; set; }

    [BindProperty]
    [Required, MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    [MaxLength(512)]
    public string? Description { get; set; }

    [BindProperty]
    public List<MaterialSetItemInput> Items { get; set; } = new();

    public List<(string Value, string Text)> MaterialsForScript { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null) return NotFound();
        MaterialSet = await _context.MaterialSets
            .Include(s => s.Items)
            .ThenInclude(i => i.Material)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);
        if (MaterialSet == null) return NotFound();

        Name = MaterialSet.Name;
        Description = MaterialSet.Description;
        Items = MaterialSet.Items.OrderBy(i => i.SortOrder).Select(i => new MaterialSetItemInput { MaterialId = i.MaterialId, Quantity = i.Quantity }).ToList();
        if (Items.Count == 0) Items.Add(new MaterialSetItemInput());

        var materials = await _context.Materials.OrderBy(m => m.Name).ToListAsync();
        MaterialsForScript = materials
            .Select(m =>
            {
                var label = string.IsNullOrWhiteSpace(m.Reference)
                    ? m.Name
                    : $"{m.Name} - {m.Reference}";
                return (m.Id.ToString(), label);
            })
            .ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (id == null) return NotFound();
        var set = await _context.MaterialSets.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == id);
        if (set == null) return NotFound();

        var validItems = Items?.Where(i => i.MaterialId > 0 && i.Quantity > 0).ToList() ?? new List<MaterialSetItemInput>();
        if (validItems.Count == 0)
            ModelState.AddModelError("", "Add at least one material with quantity greater than 0.");

        if (!ModelState.IsValid)
        {
            MaterialSet = set;
            var materials = await _context.Materials.OrderBy(m => m.Name).ToListAsync();
            MaterialsForScript = materials
                .Select(m =>
                {
                    var label = string.IsNullOrWhiteSpace(m.Reference)
                        ? m.Name
                        : $"{m.Name} - {m.Reference}";
                    return (m.Id.ToString(), label);
                })
                .ToList();
            return Page();
        }

        set.Name = Name.Trim();
        set.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        _context.MaterialSetItems.RemoveRange(set.Items);
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

        TempData["Success"] = $"Material set \"{set.Name}\" updated.";
        return RedirectToPage("./Index");
    }
}
