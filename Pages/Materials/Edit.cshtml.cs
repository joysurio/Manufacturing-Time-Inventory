using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Inventory;
using ManufacturingTimeTracking.Models.Templates;
using System.Security.Claims;

namespace ManufacturingTimeTracking.Pages.Materials;

public class EditModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public EditModel(ApplicationDbContext context, IConfiguration configuration, IWebHostEnvironment environment)
    {
        _context = context;
        _configuration = configuration;
        _environment = environment;
    }

    private async Task<string> GetPerformedByAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var email = await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(email))
                return email;
        }

        return User.Identity?.Name ?? "";
    }

    [BindProperty]
    public Material Material { get; set; } = default!;

    [BindProperty]
    public IFormFile? ImageFile { get; set; }

    /// <summary>Stock quantity at the selected location (reception).</summary>
    [BindProperty]
    public int Stock { get; set; }

    public SelectList Suppliers { get; set; } = default!;
    public SelectList Locations { get; set; } = default!;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var material = await _context.Materials.FirstOrDefaultAsync(m => m.Id == id);
        if (material == null)
        {
            return NotFound();
        }
        Material = material;
        Stock = material.LocationId.HasValue
            ? await _context.MaterialStocks.Where(ms => ms.MaterialId == material.Id && ms.LocationId == material.LocationId.Value).SumAsync(ms => ms.Quantity)
            : 0;
        Suppliers = new SelectList(await _context.Suppliers.OrderBy(s => s.Name).ToListAsync(), "Id", "Name", material.SupplierId);
        Locations = new SelectList(await _context.Locations.OrderBy(l => l.Code).ToListAsync(), "Id", "Code", material.LocationId);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Stock > 0 && !Material.LocationId.HasValue)
            ModelState.AddModelError(nameof(Stock), "Select a location when entering stock.");
        if (!ModelState.IsValid)
        {
            Suppliers = new SelectList(await _context.Suppliers.OrderBy(s => s.Name).ToListAsync(), "Id", "Name", Material.SupplierId);
            Locations = new SelectList(await _context.Locations.OrderBy(l => l.Code).ToListAsync(), "Id", "Code", Material.LocationId);
            return Page();
        }

        // Handle image upload
        if (ImageFile != null && ImageFile.Length > 0)
        {
            var extension = Path.GetExtension(ImageFile.FileName).ToLower();
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };
            
            if (allowedExtensions.Contains(extension))
            {
                // Save under WebRootPath so files are served by UseStaticFiles (avoids 404 when running from VS)
                var uploadsRoot = Path.Combine(_environment.WebRootPath ?? "", "uploads");
                var materialsPath = Path.Combine(uploadsRoot, "materials");

                if (!Directory.Exists(materialsPath))
                {
                    Directory.CreateDirectory(materialsPath);
                }

                var fileName = $"{Guid.NewGuid()}{extension}";
                var filePath = Path.Combine(materialsPath, fileName);
                var relativePath = Path.Combine("materials", fileName).Replace('\\', '/');

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await ImageFile.CopyToAsync(stream);
                }

                Material.ImageUrl = relativePath;
            }
        }

        _context.Attach(Material).State = EntityState.Modified;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!MaterialExists(Material.Id))
            {
                return NotFound();
            }
            else
            {
                throw;
            }
        }

        if (Material.LocationId.HasValue)
        {
            var existing = await _context.MaterialStocks
                .FirstOrDefaultAsync(ms => ms.MaterialId == Material.Id && ms.LocationId == Material.LocationId.Value);
            var previousQty = existing?.Quantity ?? 0;
            if (existing != null)
            {
                existing.Quantity = Stock;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
            else if (Stock > 0)
            {
                _context.MaterialStocks.Add(new MaterialStock
                {
                    MaterialId = Material.Id,
                    LocationId = Material.LocationId.Value,
                    Quantity = Stock,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }
            await _context.SaveChangesAsync();

            var delta = Stock - previousQty;
            if (delta != 0)
            {
                _context.MaterialStockMovements.Add(new MaterialStockMovement
                {
                    MaterialId = Material.Id,
                    LocationId = Material.LocationId.Value,
                    QuantityDelta = delta,
                    Type = "ADJUST",
                    Notes = "Manual stock adjustment",
                    PerformedBy = await GetPerformedByAsync(),
                    TimestampUtc = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }
        }

        return RedirectToPage("./Index");
    }

    private bool MaterialExists(int id)
    {
        return _context.Materials.Any(e => e.Id == id);
    }
}
