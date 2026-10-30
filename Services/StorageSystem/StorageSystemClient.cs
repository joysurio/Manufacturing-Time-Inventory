using CareFusion.Lib.StorageSystem;
using CareFusion.Lib.StorageSystem.Output;
using CareFusion.Lib.StorageSystem.Stock;
using Microsoft.Extensions.Options;
using System.Net.Sockets;

namespace ManufacturingTimeTracking.Services.StorageSystem;

public sealed class StorageSystemClient : IStorageSystemClient
{
    private static readonly object ConnectionLock = new();
    private static int _orderNumberSeed = Environment.TickCount;

    private readonly StorageSystemOptions _options;
    private readonly ILogger<StorageSystemClient> _logger;

    public StorageSystemClient(IOptions<StorageSystemOptions> options, ILogger<StorageSystemClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<StorageStockItemDto>> GetStockAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
            Execute(storage =>
            {
                var articles = storage.GetStock(_options.IncludePacks, _options.IncludeArticleDetails);
                var mapped = MapStock(articles);
                _logger.LogInformation(
                    "IT.Storage stock: {RawCount} article(s) from robot, {AvailableCount} with quantity > 0",
                    articles.Count,
                    mapped.Count);
                return mapped;
            }),
            cancellationToken);
    }

    public Task<StoragePickResult> RequestPickAsync(IEnumerable<string> articleIds, CancellationToken cancellationToken = default)
    {
        var ids = articleIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0)
        {
            return Task.FromResult(new StoragePickResult());
        }

        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
            Execute(storage =>
            {
                var succeeded = new List<string>();
                var failed = new List<StoragePickFailure>();

                foreach (var articleId in ids)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var orderNumber = Interlocked.Increment(ref _orderNumberSeed);
                        var output = storage.CreateOutputProcess(
                            orderNumber,
                            _options.OutputDestination,
                            _options.OutputPoint);
                        output.AddCriteria(articleId, 1);
                        output.Start();

                        if (IsPickAccepted(output.State))
                        {
                            succeeded.Add(articleId);
                        }
                        else
                        {
                            failed.Add(new StoragePickFailure
                            {
                                ArticleId = articleId,
                                Error = $"Pick rejected (state: {output.State})."
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Pick failed for article {ArticleId}", articleId);
                        failed.Add(new StoragePickFailure
                        {
                            ArticleId = articleId,
                            Error = ex.Message
                        });
                    }
                }

                return new StoragePickResult
                {
                    Succeeded = succeeded,
                    Failed = failed
                };
            }),
            cancellationToken);
    }

    private T Execute<T>(Func<RowaStorageSystem, T> action)
    {
        lock (ConnectionLock)
        {
            RowaStorageSystem? storage = null;
            try
            {
                storage = new RowaStorageSystem(_options.SubscriberId);
                storage.Connect(_options.Host, _options.Port);
                return action(storage);
            }
            catch (SocketException ex)
            {
                throw new InvalidOperationException(
                    $"Cannot connect to storage system at {_options.Host}:{_options.Port}. Ensure IT.Storage robot is running.",
                    ex);
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"Cannot connect to storage system at {_options.Host}:{_options.Port}. {ex.Message}",
                    ex);
            }
            finally
            {
                storage?.Disconnect();
            }
        }
    }

    private static IReadOnlyList<StorageStockItemDto> MapStock(List<IArticle> articles)
    {
        return articles
            .Select(a => new StorageStockItemDto
            {
                ArticleId = a.Id ?? "",
                Name = a.Name ?? "",
                DosageForm = a.DosageForm ?? "",
                PackagingUnit = a.PackagingUnit ?? "",
                QuantityAvailable = (int)a.PackCount
            })
            .Where(a => !string.IsNullOrWhiteSpace(a.ArticleId) && a.QuantityAvailable > 0)
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.ArticleId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsPickAccepted(OutputProcessState state) =>
        state is OutputProcessState.Queued
            or OutputProcessState.InProcess
            or OutputProcessState.Completed
            or OutputProcessState.Incomplete
            or OutputProcessState.BoxReleased;
}
