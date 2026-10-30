using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Inventory;
using ManufacturingTimeTracking.Models.Templates;
using System.Security.Claims;

namespace ManufacturingTimeTracking.Pages.Materials;

public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public CreateModel(ApplicationDbContext context, IConfiguration configuration, IWebHostEnvironment environment)
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

    /// <summary>Initial stock quantity (reception at the selected location). Requires Location when &gt; 0.</summary>
    [BindProperty]
    public int Stock { get; set; }

    public SelectList Suppliers { get; set; } = default!;
    public SelectList Locations { get; set; } = default!;

    public async Task<IActionResult> OnGetAsync()
    {
        Suppliers = new SelectList(await _context.Suppliers.OrderBy(s => s.Name).ToListAsync(), "Id", "Name");
        Locations = new SelectList(await _context.Locations.OrderBy(l => l.Code).ToListAsync(), "Id", "Code");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Stock > 0 && !Material.LocationId.HasValue)
            ModelState.AddModelError(nameof(Stock), "Select a location when entering initial stock.");
        if (!ModelState.IsValid)
        {
            Suppliers = new SelectList(await _context.Suppliers.OrderBy(s => s.Name).ToListAsync(), "Id", "Name");
            Locations = new SelectList(await _context.Locations.OrderBy(l => l.Code).ToListAsync(), "Id", "Code");
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

        _context.Materials.Add(Material);
        await _context.SaveChangesAsync();

        if (Stock > 0 && Material.LocationId.HasValue)
        {
            _context.MaterialStocks.Add(new MaterialStock
            {
                MaterialId = Material.Id,
                LocationId = Material.LocationId.Value,
                Quantity = Stock,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            _context.MaterialStockMovements.Add(new MaterialStockMovement
            {
                MaterialId = Material.Id,
                LocationId = Material.LocationId.Value,
                QuantityDelta = Stock,
                Type = "ADJUST",
                Notes = "Manual stock adjustment",
                PerformedBy = await GetPerformedByAsync(),
                TimestampUtc = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        return RedirectToPage("./Index");
    }
}
