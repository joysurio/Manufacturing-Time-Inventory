namespace ManufacturingTimeTracking.Services.StorageSystem;

public class StorageSystemOptions
{
    public const string SectionName = "StorageSystem";

    public string Host { get; set; } = "127.0.0.1";
    public ushort Port { get; set; } = 6050;
    public int SubscriberId { get; set; } = 100;
    public int OutputDestination { get; set; } = 1;
    public int OutputPoint { get; set; }
    public bool IncludePacks { get; set; } = true;
    public bool IncludeArticleDetails { get; set; }
}
