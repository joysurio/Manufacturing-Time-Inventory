using System.ComponentModel.DataAnnotations;

namespace ManufacturingTimeTracking.Models.Inventory;

public class Location
{
    public int Id { get; set; }

    [Required, MaxLength(64)]
    public string Code { get; set; } = "";

    [MaxLength(64)]
    public string Zone { get; set; } = "Z1";

    public int X { get; set; }
    public int Y { get; set; }
    public int? Z { get; set; }

    [MaxLength(32)]
    public string Type { get; set; } = "Shelf"; // Shelf, RFIDCabinet, Reception, Output, Quarantine, Robot, Workstation

    public int CapacityUnits { get; set; } = 100;
    public bool IsBlocked { get; set; } = false;
}
