using ManufacturingTimeTracking.Models.Catalog;
using ManufacturingTimeTracking.Models.Inventory;

namespace ManufacturingTimeTracking.Models.Templates;

public class Material
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public string? ImageUrl { get; set; }
    /// <summary>QR code value: enterable manually or generated.</summary>
    public string? Qr { get; set; }
    /// <summary>Price (e.g. unit price).</summary>
    public decimal? Price { get; set; }
    public int? SupplierId { get; set; }
    /// <summary>Supplier reference / part number.</summary>
    public string? Reference { get; set; }
    /// <summary>Free-text notes about this material.</summary>
    public string? Notes { get; set; }
    /// <summary>Warehouse/location where this material is stored.</summary>
    public int? LocationId { get; set; }

    /// <summary>Minimum stock level; alert when current stock falls below this (default 0).</summary>
    public int MinimumStock { get; set; }

    // Navigation
    public Supplier? Supplier { get; set; }
    public Location? Location { get; set; }
    public ICollection<StepTemplateMaterial> StepTemplates { get; set; } = new List<StepTemplateMaterial>();
    /// <summary>Inventory items linked to this material (same product in warehouse and production).</summary>
    public ICollection<Item> InventoryItems { get; set; } = new List<Item>();
}
