using System.ComponentModel.DataAnnotations;
using ManufacturingTimeTracking.Models.Execution;
using ManufacturingTimeTracking.Models.Templates;

namespace ManufacturingTimeTracking.Models.Inventory;

/// <summary>Ledger of material stock changes: receptions (IN), consumption (OUT), manual adjustments (ADJUST).</summary>
public class MaterialStockMovement
{
    public int Id { get; set; }

    public int MaterialId { get; set; }
    public Material? Material { get; set; }

    public int LocationId { get; set; }
    public Location? Location { get; set; }

    /// <summary>Change in quantity: positive = in, negative = out.</summary>
    public int QuantityDelta { get; set; }

    [MaxLength(32)]
    public string Type { get; set; } = "ADJUST"; // IN, OUT, ADJUST

    [MaxLength(512)]
    public string? Notes { get; set; }

    [MaxLength(256)]
    public string PerformedBy { get; set; } = "";

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When Type=OUT and destination is an order.</summary>
    public int? BuildOrderId { get; set; }
    public BuildOrder? BuildOrder { get; set; }

    /// <summary>When Type=OUT and destination is a client (free text).</summary>
    [MaxLength(256)]
    public string? DestinationClient { get; set; }
}
