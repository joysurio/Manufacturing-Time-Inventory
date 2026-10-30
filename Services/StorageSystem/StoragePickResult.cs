namespace ManufacturingTimeTracking.Services.StorageSystem;

public sealed class StoragePickResult
{
    public IReadOnlyList<string> Succeeded { get; init; } = Array.Empty<string>();
    public IReadOnlyList<StoragePickFailure> Failed { get; init; } = Array.Empty<StoragePickFailure>();
}

public sealed class StoragePickFailure
{
    public string ArticleId { get; init; } = "";
    public string Error { get; init; } = "";
}
