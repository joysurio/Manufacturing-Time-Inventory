using ManufacturingTimeTracking.Models.Inventory;

namespace ManufacturingTimeTracking.Models.Execution;

/// <summary>
/// One line of the pick list for a build order. Tracks how many have been picked (scan QR) so the list can be opened/updated over time.
/// </summary>
public class OrderPickLine
{
    public int Id { get; set; }
    public int BuildOrderId { get; set; }
    public int ItemId { get; set; }
    public int QuantityRequired { get; set; }
    public int QuantityPicked { get; set; }

    public BuildOrder BuildOrder { get; set; } = null!;
    public Item Item { get; set; } = null!;
}
