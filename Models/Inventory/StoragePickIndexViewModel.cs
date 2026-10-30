namespace ManufacturingTimeTracking.Models.Inventory;

public class StoragePickIndexViewModel
{
    public List<StorageStockRowViewModel> Rows { get; set; } = new();
    public bool StockLoaded { get; set; }
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
}

public class StorageStockRowViewModel
{
    public string ArticleId { get; set; } = "";
    public string Name { get; set; } = "";
    public string DosageForm { get; set; } = "";
    public string PackagingUnit { get; set; } = "";
    public int QuantityAvailable { get; set; }
    public bool Selected { get; set; }
}
