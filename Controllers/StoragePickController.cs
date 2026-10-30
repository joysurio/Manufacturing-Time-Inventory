using ManufacturingTimeTracking.Models.Inventory;
using ManufacturingTimeTracking.Services.StorageSystem;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ManufacturingTimeTracking.Controllers;

[Authorize(Policy = "CanUseInventory")]
public class StoragePickController : Controller
{
    private readonly IStorageSystemClient _storageClient;
    private readonly ILogger<StoragePickController> _logger;

    public StoragePickController(IStorageSystemClient storageClient, ILogger<StoragePickController> logger)
    {
        _storageClient = storageClient;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(new StoragePickIndexViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoadStock(CancellationToken cancellationToken)
    {
        try
        {
            var items = await _storageClient.GetStockAsync(cancellationToken);
            var model = new StoragePickIndexViewModel
            {
                StockLoaded = true,
                StatusMessage = items.Count == 0
                    ? "Connected to IT.Storage, but no stock with available quantity was found. " +
                      "The robot may be empty (e.g. after a pick). For dev testing, restart Test-WWKS tools/SeedRobotHost, then load again."
                    : $"Loaded {items.Count} article(s) from IT.Storage.",
                Rows = items.Select(MapRow).ToList()
            };
            return View("Index", model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load stock from IT.Storage");
            return View("Index", new StoragePickIndexViewModel
            {
                ErrorMessage = ex.Message
            });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestPick(
        List<string>? selectedArticleIds,
        CancellationToken cancellationToken)
    {
        var ids = selectedArticleIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        if (ids.Count == 0)
        {
            TempData["ToastrWarning"] = "Select at least one article to request a pick.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var result = await _storageClient.RequestPickAsync(ids, cancellationToken);

            if (result.Succeeded.Count > 0)
            {
                TempData["ToastrSuccess"] =
                    $"Pick order queued for {result.Succeeded.Count} article(s): {string.Join(", ", result.Succeeded)}.";
            }

            if (result.Failed.Count > 0)
            {
                var details = string.Join("; ", result.Failed.Select(f => $"{f.ArticleId}: {f.Error}"));
                TempData["ToastrError"] = $"Pick failed for {result.Failed.Count} article(s): {details}";
            }

            if (result.Succeeded.Count == 0 && result.Failed.Count == 0)
            {
                TempData["ToastrWarning"] = "No pick orders were sent.";
            }

            var stockAfter = await _storageClient.GetStockAsync(cancellationToken);
            var model = BuildModelFromStock(stockAfter, ids);
            model.StockLoaded = true;
            model.StatusMessage = $"Pick request sent. {stockAfter.Count} article(s) still available in IT.Storage.";
            return View("Index", model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to request pick from IT.Storage");
            TempData["ToastrError"] = ex.Message;
            try
            {
                var stockAfter = await _storageClient.GetStockAsync(cancellationToken);
                var errorModel = BuildModelFromStock(stockAfter, ids);
                errorModel.StockLoaded = true;
                errorModel.ErrorMessage = ex.Message;
                return View("Index", errorModel);
            }
            catch
            {
                return View("Index", new StoragePickIndexViewModel { ErrorMessage = ex.Message });
            }
        }
    }

    private static StorageStockRowViewModel MapRow(StorageStockItemDto item) => new()
    {
        ArticleId = item.ArticleId,
        Name = item.Name,
        DosageForm = item.DosageForm,
        PackagingUnit = item.PackagingUnit,
        QuantityAvailable = item.QuantityAvailable
    };

    private static StoragePickIndexViewModel BuildModelFromStock(
        IReadOnlyList<StorageStockItemDto> stock,
        IReadOnlyCollection<string> selectedIds)
    {
        var selected = new HashSet<string>(selectedIds, StringComparer.OrdinalIgnoreCase);
        return new StoragePickIndexViewModel
        {
            Rows = stock.Select(item => new StorageStockRowViewModel
            {
                ArticleId = item.ArticleId,
                Name = item.Name,
                DosageForm = item.DosageForm,
                PackagingUnit = item.PackagingUnit,
                QuantityAvailable = item.QuantityAvailable,
                Selected = selected.Contains(item.ArticleId)
            }).ToList()
        };
    }
}
