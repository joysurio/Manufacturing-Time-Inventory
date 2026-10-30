namespace ManufacturingTimeTracking.Services.StorageSystem;

public sealed class StorageStockItemDto
{
    public string ArticleId { get; set; } = "";
    public string Name { get; set; } = "";
    public string DosageForm { get; set; } = "";
    public string PackagingUnit { get; set; } = "";
    public int QuantityAvailable { get; set; }
}
