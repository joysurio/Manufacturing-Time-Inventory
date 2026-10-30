namespace ManufacturingTimeTracking.Models.Catalog;

public class Workstation
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public bool Active { get; set; } = true;
}
