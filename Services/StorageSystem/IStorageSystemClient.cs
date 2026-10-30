namespace ManufacturingTimeTracking.Services.StorageSystem;

public interface IStorageSystemClient
{
    Task<IReadOnlyList<StorageStockItemDto>> GetStockAsync(CancellationToken cancellationToken = default);
    Task<StoragePickResult> RequestPickAsync(IEnumerable<string> articleIds, CancellationToken cancellationToken = default);
}
