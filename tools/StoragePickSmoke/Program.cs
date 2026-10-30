using ManufacturingTimeTracking.Services.StorageSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.Configure<StorageSystemOptions>(o =>
{
    o.Host = "127.0.0.1";
    o.Port = 6050;
    o.SubscriberId = 100;
    o.OutputDestination = 1;
    o.OutputPoint = 0;
    o.IncludePacks = true;
    o.IncludeArticleDetails = true;
});
services.AddSingleton<IStorageSystemClient, StorageSystemClient>();

var client = services.BuildServiceProvider().GetRequiredService<IStorageSystemClient>();

Console.WriteLine("Storage Pick smoke test — 127.0.0.1:6050");
Console.WriteLine("(Start robot first: Test-WWKS tools/SeedRobotHost or PharmacyRobot.Host)");
Console.WriteLine();

try
{
    var stock = await client.GetStockAsync();
    Console.WriteLine($"GetStock: {stock.Count} article(s) with available quantity.");
    foreach (var item in stock)
        Console.WriteLine($"  {item.ArticleId} | {item.Name} | qty={item.QuantityAvailable}");

    if (stock.Count == 0)
    {
        Console.WriteLine();
        Console.WriteLine("No stock. Start SeedRobotHost, then run this test again.");
        return 1;
    }

    var pickId = stock[0].ArticleId;
    Console.WriteLine();
    Console.WriteLine($"RequestPick: {pickId} (qty 1) ...");
    var result = await client.RequestPickAsync(new[] { pickId });
    if (result.Succeeded.Count > 0)
        Console.WriteLine($"Succeeded: {string.Join(", ", result.Succeeded)}");
    foreach (var f in result.Failed)
        Console.WriteLine($"Failed: {f.ArticleId} — {f.Error}");

    if (result.Failed.Count > 0)
        return 1;

    Console.WriteLine("Smoke test OK.");
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR: {ex.Message}");
    return 1;
}
