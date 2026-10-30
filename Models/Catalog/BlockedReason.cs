namespace ManufacturingTimeTracking.Models.Catalog;

public class BlockedReason
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
}
