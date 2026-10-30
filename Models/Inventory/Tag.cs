using System.ComponentModel.DataAnnotations;

namespace ManufacturingTimeTracking.Models.Inventory;

public class Tag
{
    public int Id { get; set; }

    [Required, MaxLength(128)]
    public string Code { get; set; } = "";

    [Required, MaxLength(16)]
    public string TagType { get; set; } = "RFID";

    public int PackQuantity { get; set; } = 1;

    public int ItemId { get; set; }
    public Item? Item { get; set; }
}
