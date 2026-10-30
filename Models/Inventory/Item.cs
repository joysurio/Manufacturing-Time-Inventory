using System.ComponentModel.DataAnnotations;

namespace ManufacturingTimeTracking.Models.Inventory;

/// <summary>
/// Inventory item. Optionally linked to a process Material so the same product is shared between warehouse and production.
/// </summary>
public class Item
{
    public int Id { get; set; }

    [Required, MaxLength(64)]
    public string Sku { get; set; } = "";

    [Required, MaxLength(256)]
    public string Name { get; set; } = "";

    [MaxLength(256)]
    public string Family { get; set; } = "";

    /// <summary>Model or type for reporting and machine component lists (e.g. Motor, Sensor, PC, D600).</summary>
    [MaxLength(64)]
    public string ModelOrType { get; set; } = "";

    [MaxLength(32)]
    public string Unit { get; set; } = "ud";

    public bool IsSerialized { get; set; } = false;

    [MaxLength(512)]
    public string ImagePath { get; set; } = "";

    /// <summary>
    /// Link to process Material so this item can be used in BOM and moved to production stations.
    /// </summary>
    public int? MaterialId { get; set; }
    public Templates.Material? Material { get; set; }

    public List<Tag> Tags { get; set; } = new();
}
