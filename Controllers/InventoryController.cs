using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Execution;
using ManufacturingTimeTracking.Models.Inventory;
using ManufacturingTimeTracking.Models.Templates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ManufacturingTimeTracking.Controllers;

[Authorize(Policy = "CanUseInventory")]
public class InventoryController : Controller
{
    private readonly ApplicationDbContext _db;

    public InventoryController(ApplicationDbContext db) => _db = db;

    private async Task<string> GetPerformedByAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var email = await _db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(email))
                return email;
        }
        return User.Identity?.Name ?? "";
    }

    public async Task<IActionResult> Receive()
    {
        var materials = await _db.Materials
            .Include(m => m.Location)
            .OrderBy(m => m.Name)
            .ToListAsync();
        var locations = await _db.Locations.OrderBy(l => l.Code).ToListAsync();
        ViewBag.Materials = materials;
        ViewBag.Locations = new SelectList(locations, "Id", "Code");
        return View(new ReceiveViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(ReceiveViewModel model)
    {
        if (model.MaterialId <= 0 || model.LocationId <= 0 || model.Quantity <= 0)
        {
            ModelState.AddModelError("", "Please select a material, location, and enter a quantity greater than 0.");
        }

        if (!ModelState.IsValid)
        {
            var materials = await _db.Materials.Include(m => m.Location).OrderBy(m => m.Name).ToListAsync();
            var locations = await _db.Locations.OrderBy(l => l.Code).ToListAsync();
            ViewBag.Materials = materials;
            ViewBag.Locations = new SelectList(locations, "Id", "Code", model.LocationId);
            return View(model);
        }

        var material = await _db.Materials.FindAsync(model.MaterialId);
        var materialName = material?.Name ?? "Material";

        var existing = await _db.MaterialStocks
            .FirstOrDefaultAsync(ms => ms.MaterialId == model.MaterialId && ms.LocationId == model.LocationId);
        if (existing != null)
        {
            existing.Quantity += model.Quantity;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            _db.MaterialStocks.Add(new MaterialStock
            {
                MaterialId = model.MaterialId,
                LocationId = model.LocationId,
                Quantity = model.Quantity,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync();

        _db.MaterialStockMovements.Add(new MaterialStockMovement
        {
            MaterialId = model.MaterialId,
            LocationId = model.LocationId,
            QuantityDelta = model.Quantity,
            Type = "IN",
            Notes = "Reception",
            PerformedBy = await GetPerformedByAsync(),
            TimestampUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        TempData["ToastrSuccess"] = $"{model.Quantity} units of {materialName} have been received.";
        return RedirectToAction(nameof(Stock));
    }

    public async Task<IActionResult> MaterialOut()
    {
        var materials = await _db.Materials
            .OrderBy(m => m.Name)
            .Select(m => new { m.Id, m.Name, m.Reference, m.Qr })
            .ToListAsync();
        var materialSets = await _db.MaterialSets
            .Include(s => s.Items)
            .ThenInclude(i => i.Material)
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, ItemCount = s.Items.Count })
            .ToListAsync();
        var stockByMaterialLocation = await _db.MaterialStocks
            .Include(ms => ms.Location)
            .Where(ms => ms.Quantity > 0)
            .Select(ms => new { ms.MaterialId, ms.LocationId, LocationCode = ms.Location != null ? ms.Location.Code : "", ms.Quantity })
            .ToListAsync();
        var activeOrders = await _db.BuildOrders
            .Where(o => o.Status != "Completed" && o.Status != "Cancelled")
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new { o.Id, Display = o.SerialNumber + " - " + (o.ExternalRef ?? ""), o.SerialNumber, o.ExternalRef })
            .Take(500)
            .ToListAsync();
        ViewBag.Materials = materials;
        ViewBag.MaterialSets = materialSets;
        ViewBag.StockByMaterialLocation = stockByMaterialLocation;
        ViewBag.ActiveOrders = activeOrders;
        return View(new MaterialOutViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MaterialOut(MaterialOutViewModel model)
    {
        if (model.DestinationKind == "Order" && (model.BuildOrderId == null || model.BuildOrderId <= 0))
            ModelState.AddModelError("", "Please select an order when destination is Order.");
        if (model.DestinationKind == "Client" && string.IsNullOrWhiteSpace(model.DestinationClient))
            ModelState.AddModelError("", "Please enter a client name when destination is Client.");

        if (model.MaterialSetId.HasValue && model.MaterialSetId.Value > 0)
        {
            // Material set: expand set and process each material
            var set = await _db.MaterialSets
                .Include(s => s.Items)
                .ThenInclude(i => i.Material)
                .FirstOrDefaultAsync(s => s.Id == model.MaterialSetId.Value);
            if (set == null || !set.Items.Any())
            {
                ModelState.AddModelError("", "Selected material set not found or has no materials.");
            }
            else
            {
                // Preload stock for all materials in the set so we can decide per-material location.
                var materialIds = set.Items.Select(i => i.MaterialId).Distinct().ToList();
                var stockForSet = await _db.MaterialStocks
                    .Include(ms => ms.Location)
                    .Where(ms => materialIds.Contains(ms.MaterialId) && ms.Quantity > 0)
                    .ToListAsync();
                var stockByMaterial = stockForSet
                    .GroupBy(ms => ms.MaterialId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var errors = new List<string>();
                foreach (var item in set.Items)
                {
                    var name = item.Material?.Name ?? "Material";

                    stockByMaterial.TryGetValue(item.MaterialId, out var stocksForMaterial);
                    stocksForMaterial ??= new List<MaterialStock>();

                    if (!stocksForMaterial.Any())
                    {
                        errors.Add($"{name}: no stock available in any location.");
                        continue;
                    }

                    // Single-location material: auto-use that location.
                    if (stocksForMaterial.Count == 1)
                    {
                        var s = stocksForMaterial[0];
                        if (s.Quantity < item.Quantity)
                        {
                            errors.Add($"{name}: need {item.Quantity}, available at location {s.Location?.Code ?? "?"}: {s.Quantity}");
                        }
                        continue;
                    }

                    // Multiple locations: require user to choose a location for this material.
                    if (!model.LocationId.HasValue || model.LocationId.Value <= 0)
                    {
                        var locList = string.Join(", ",
                            stocksForMaterial.Select(s => $"{s.Location?.Code ?? "?"} ({s.Quantity})"));
                        errors.Add(
                            $"{name}: stored in multiple locations ({locList}). Please select a location to use for this material.");
                        continue;
                    }

                    var chosen = stocksForMaterial.FirstOrDefault(s => s.LocationId == model.LocationId);
                    if (chosen == null || chosen.Quantity < item.Quantity)
                    {
                        var available = chosen?.Quantity ?? 0;
                        errors.Add($"{name}: need {item.Quantity}, available at location: {available}");
                    }
                }
                if (errors.Count > 0)
                    ModelState.AddModelError("", "Insufficient stock for one or more materials in the set: " + string.Join("; ", errors));
                else if (ModelState.IsValid)
                {
                    var processed = new List<string>();
                    foreach (var item in set.Items)
                    {
                        stockByMaterial.TryGetValue(item.MaterialId, out var stocksForMaterial);
                        stocksForMaterial ??= new List<MaterialStock>();
                        if (!stocksForMaterial.Any()) continue;

                        MaterialStock? selectedStock;
                        if (stocksForMaterial.Count == 1)
                        {
                            selectedStock = stocksForMaterial[0];
                        }
                        else
                        {
                            // Multiple locations: use the user-selected location; validation above guarantees it exists and has enough.
                            selectedStock = stocksForMaterial.FirstOrDefault(s => s.LocationId == model.LocationId);
                        }
                        if (selectedStock == null || selectedStock.Quantity < item.Quantity) continue;

                        selectedStock.Quantity -= item.Quantity;
                        selectedStock.UpdatedAtUtc = DateTime.UtcNow;
                        _db.MaterialStockMovements.Add(new MaterialStockMovement
                        {
                            MaterialId = item.MaterialId,
                            LocationId = selectedStock.LocationId,
                            QuantityDelta = -item.Quantity,
                            Type = "OUT",
                            Notes = (model.Notes?.Trim() ?? "") + " [Set: " + set.Name + "]",
                            PerformedBy = await GetPerformedByAsync(),
                            TimestampUtc = DateTime.UtcNow,
                            BuildOrderId = model.DestinationKind == "Order" ? model.BuildOrderId : null,
                            DestinationClient = model.DestinationKind == "Client" ? model.DestinationClient?.Trim() : null
                        });
                        if (model.DestinationKind == "Order" && model.BuildOrderId.HasValue && model.BuildOrderId.Value > 0)
                            await UpdateOrderPickListForMaterialOutAsync(model.BuildOrderId.Value, item.MaterialId, item.Quantity);
                        processed.Add($"{item.Material?.Name ?? "Material"} x{item.Quantity}");
                    }
                    await _db.SaveChangesAsync();
                    var destText = model.DestinationKind == "Order" ? "order " + model.BuildOrderId : "client " + (model.DestinationClient ?? "");
                    TempData["ToastrSuccess"] = $"Material set \"{set.Name}\" recorded as out: " + string.Join(", ", processed) + " (destination: " + destText + ").";
                    return RedirectToAction(nameof(Movements));
                }
            }
        }
        else
        {
            // Single material out.
            // If no location was selected, but the material has stock in exactly one location,
            // auto-use that location. Only require manual selection when there are multiple locations.
            if (!model.LocationId.HasValue || model.LocationId.Value <= 0)
            {
                if (model.MaterialId > 0)
                {
                    var stocksForMaterial = await _db.MaterialStocks
                        .Where(ms => ms.MaterialId == model.MaterialId && ms.Quantity > 0)
                        .ToListAsync();
                    if (stocksForMaterial.Count == 1)
                    {
                        model.LocationId = stocksForMaterial[0].LocationId;
                    }
                    else
                    {
                        ModelState.AddModelError("", "Please select a location.");
                    }
                }
                else
                {
                    ModelState.AddModelError("", "Please select a location.");
                }
            }
            if (model.MaterialId <= 0)
                ModelState.AddModelError("", "Please select a material or a material set.");
            else if (model.Quantity <= 0)
                ModelState.AddModelError("", "Quantity must be greater than 0.");

            if (ModelState.IsValid)
            {
                var stock = await _db.MaterialStocks
                    .FirstOrDefaultAsync(ms => ms.MaterialId == model.MaterialId && ms.LocationId == model.LocationId);
                if (stock == null || stock.Quantity < model.Quantity)
                {
                    var available = stock?.Quantity ?? 0;
                    ModelState.AddModelError("", $"Insufficient stock. Available at this location: {available}. You requested {model.Quantity}.");
                }
                else
                {
                    var material = await _db.Materials.FindAsync(model.MaterialId);
                    var materialName = material?.Name ?? "Material";
                    stock!.Quantity -= model.Quantity;
                    stock.UpdatedAtUtc = DateTime.UtcNow;

                    var movement = new MaterialStockMovement
                    {
                        MaterialId = model.MaterialId,
                LocationId = model.LocationId!.Value,
                        QuantityDelta = -model.Quantity,
                        Type = "OUT",
                        Notes = model.Notes?.Trim() ?? "",
                        PerformedBy = await GetPerformedByAsync(),
                        TimestampUtc = DateTime.UtcNow,
                        BuildOrderId = model.DestinationKind == "Order" ? model.BuildOrderId : null,
                        DestinationClient = model.DestinationKind == "Client" ? model.DestinationClient?.Trim() : null
                    };
                    _db.MaterialStockMovements.Add(movement);
                    await _db.SaveChangesAsync();

                    if (model.DestinationKind == "Order" && model.BuildOrderId.HasValue && model.BuildOrderId.Value > 0)
                        await UpdateOrderPickListForMaterialOutAsync(model.BuildOrderId.Value, model.MaterialId, model.Quantity);

                    var destText = model.DestinationKind == "Order"
                        ? "order " + (model.BuildOrderId?.ToString() ?? "")
                        : "client " + (model.DestinationClient ?? "");
                    TempData["ToastrSuccess"] = $"{model.Quantity} units of {materialName} recorded as out (destination: {destText}).";
                    return RedirectToAction(nameof(Movements));
                }
            }
        }

        var materials = await _db.Materials.OrderBy(m => m.Name).Select(m => new { m.Id, m.Name, m.Reference, m.Qr }).ToListAsync();
        var materialSets = await _db.MaterialSets
            .Include(s => s.Items)
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, ItemCount = s.Items.Count })
            .ToListAsync();
        var stockByMaterialLocation = await _db.MaterialStocks
            .Include(ms => ms.Location)
            .Where(ms => ms.Quantity > 0)
            .Select(ms => new { ms.MaterialId, ms.LocationId, LocationCode = ms.Location != null ? ms.Location.Code : "", ms.Quantity })
            .ToListAsync();
        var activeOrders = await _db.BuildOrders
            .Where(o => o.Status != "Completed" && o.Status != "Cancelled")
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new { o.Id, Display = o.SerialNumber + " - " + (o.ExternalRef ?? ""), o.SerialNumber, o.ExternalRef })
            .Take(500)
            .ToListAsync();
        ViewBag.Materials = materials;
        ViewBag.MaterialSets = materialSets;
        ViewBag.StockByMaterialLocation = stockByMaterialLocation;
        ViewBag.ActiveOrders = activeOrders;
        return View(model);
    }

    /// <summary>
    /// When Material Out is recorded for an order, update that order's pick list (OrderMaterialPickLine or OrderPickLine)
    /// so the pick list reflects the quantity taken out.
    /// </summary>
    private async Task UpdateOrderPickListForMaterialOutAsync(int buildOrderId, int materialId, int quantityOut)
    {
        var order = await _db.BuildOrders.FindAsync(buildOrderId);
        if (order == null) return;

        var model = await _db.MachineModels
            .Include(m => m.Components)
                .ThenInclude(c => c.Material)
            .FirstOrDefaultAsync(m => m.Id == order.MachineModelId);
        if (model == null) return;

        var remainingToApply = quantityOut;
        if (remainingToApply <= 0) return;

        foreach (var comp in model.Components.Where(c => c.MaterialId == materialId))
        {
            if (remainingToApply <= 0) break;

            if (!comp.ItemId.HasValue)
            {
                var matLine = await _db.OrderMaterialPickLines
                    .FirstOrDefaultAsync(l => l.BuildOrderId == buildOrderId && l.ComponentId == comp.Id);
                if (matLine == null)
                {
                    matLine = new OrderMaterialPickLine
                    {
                        BuildOrderId = buildOrderId,
                        ComponentId = comp.Id,
                        MaterialId = materialId,
                        QuantityRequired = comp.Quantity,
                        QuantityPicked = 0
                    };
                    _db.OrderMaterialPickLines.Add(matLine);
                }
                var need = comp.Quantity - matLine.QuantityPicked;
                var add = Math.Min(remainingToApply, Math.Max(0, need));
                if (add > 0)
                {
                    matLine.QuantityPicked += add;
                    remainingToApply -= add;
                }
            }
            else
            {
                var itemLine = await _db.OrderPickLines
                    .FirstOrDefaultAsync(l => l.BuildOrderId == buildOrderId && l.ItemId == comp.ItemId.Value);
                if (itemLine != null)
                {
                    var need = itemLine.QuantityRequired - itemLine.QuantityPicked;
                    var add = Math.Min(remainingToApply, Math.Max(0, need));
                    if (add > 0)
                    {
                        itemLine.QuantityPicked += add;
                        remainingToApply -= add;
                    }
                }
            }
        }

        if (remainingToApply > 0)
        {
            var itemLinkedToMaterial = await _db.Items
                .Where(i => i.MaterialId == materialId)
                .Select(i => i.Id)
                .FirstOrDefaultAsync();
            if (itemLinkedToMaterial != 0)
            {
                var itemLine = await _db.OrderPickLines
                    .FirstOrDefaultAsync(l => l.BuildOrderId == buildOrderId && l.ItemId == itemLinkedToMaterial);
                if (itemLine != null)
                {
                    var need = itemLine.QuantityRequired - itemLine.QuantityPicked;
                    var add = Math.Min(remainingToApply, Math.Max(0, need));
                    if (add > 0)
                    {
                        itemLine.QuantityPicked += add;
                    }
                }
            }
        }

        await _db.SaveChangesAsync();
    }

    public async Task<IActionResult> Stock(
        bool viewByLocation = false,
        string? search = null,
        string? materialSort = null,
        string? materialSortDir = null,
        string? itemSort = null,
        string? itemSortDir = null)
    {
        var itemQuery = _db.StockSnapshots
            .Include(s => s.Item)
            .ThenInclude(i => i!.Material)
            .Include(s => s.Location)
            .AsQueryable();

        var materialStock = await _db.MaterialStocks
            .Include(ms => ms.Material)
            .Include(ms => ms.Location)
            .OrderByDescending(ms => ms.UpdatedAtUtc)
            .ToListAsync();
        var totalByMaterialId = materialStock
            .GroupBy(ms => ms.MaterialId)
            .ToDictionary(g => g.Key, g => g.Sum(ms => ms.Quantity));
        var rows = materialStock
            .Select(ms => new MaterialStockRow
            {
                MaterialId = ms.MaterialId,
                LocationId = ms.LocationId,
                MaterialName = ms.Material?.Name ?? "",
                LocationCode = ms.Location?.Code ?? "",
                Quantity = ms.Quantity,
                UnitPrice = ms.Material?.Price,
                UpdatedAtUtc = ms.UpdatedAtUtc,
                MinimumStock = ms.Material?.MinimumStock ?? 0,
                TotalQuantityForMaterial = totalByMaterialId.GetValueOrDefault(ms.MaterialId, 0)
            })
            .Where(r => string.IsNullOrWhiteSpace(search) || (r.MaterialName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        var matDir = NormalizeSortDir(materialSortDir);
        // Flat "by location" table: sort line rows. Grouped-by-material table: sort aggregates (not raw rows first).
        if (viewByLocation && !string.IsNullOrWhiteSpace(materialSort))
            rows = SortMaterialStockRows(rows, materialSort.Trim(), matDir);

        var grouped = rows
            .GroupBy(r => r.MaterialName)
            .Select(g => new MaterialStockGroupedRow
            {
                MaterialId = g.First().MaterialId,
                MaterialName = g.Key,
                TotalQuantity = g.Sum(x => x.Quantity),
                UnitPrice = g.First().UnitPrice,
                TotalValue = g.First().UnitPrice.HasValue ? g.Sum(x => x.Quantity) * g.First().UnitPrice!.Value : null,
                UpdatedAtUtc = g.Max(x => x.UpdatedAtUtc),
                MinimumStock = g.First().MinimumStock
            })
            .ToList();
        if (!viewByLocation && !string.IsNullOrWhiteSpace(materialSort))
            grouped = SortMaterialStockGrouped(grouped, materialSort.Trim(), matDir);
        else if (!viewByLocation)
            grouped = grouped.OrderBy(r => r.MaterialName).ToList();

        var itemDir = NormalizeSortDir(itemSortDir);
        var itemStock = await itemQuery.ToListAsync();
        if (!string.IsNullOrWhiteSpace(itemSort))
            itemStock = SortItemStock(itemStock, itemSort.Trim(), itemDir);
        else
            itemStock = itemStock.OrderByDescending(s => s.UpdatedAtUtc).ToList();

        var model = new StockViewModel
        {
            ItemStock = itemStock,
            MaterialStock = rows,
            MaterialStockGrouped = grouped,
            ViewByLocation = viewByLocation,
            MaterialSearch = search?.Trim(),
            MaterialSort = string.IsNullOrWhiteSpace(materialSort) ? null : materialSort.Trim(),
            MaterialSortDir = matDir,
            ItemSort = string.IsNullOrWhiteSpace(itemSort) ? null : itemSort.Trim(),
            ItemSortDir = itemDir
        };
        return View(model);
    }

    private static string NormalizeSortDir(string? dir) =>
        string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";

    private static List<MaterialStockRow> SortMaterialStockRows(List<MaterialStockRow> rows, string col, string dir)
    {
        var desc = dir == "desc";
        return col.ToLowerInvariant() switch
        {
            "name" => desc ? rows.OrderByDescending(r => r.MaterialName).ToList() : rows.OrderBy(r => r.MaterialName).ToList(),
            "location" => desc ? rows.OrderByDescending(r => r.LocationCode).ToList() : rows.OrderBy(r => r.LocationCode).ToList(),
            "qty" => desc ? rows.OrderByDescending(r => r.Quantity).ToList() : rows.OrderBy(r => r.Quantity).ToList(),
            "price" => desc
                ? rows.OrderBy(r => r.UnitPrice == null).ThenByDescending(r => r.UnitPrice).ToList()
                : rows.OrderBy(r => r.UnitPrice == null).ThenBy(r => r.UnitPrice).ToList(),
            "value" => desc
                ? rows.OrderByDescending(r => MaterialStockRowLineValue(r)).ToList()
                : rows.OrderBy(r => MaterialStockRowLineValue(r)).ToList(),
            "updated" => desc ? rows.OrderByDescending(r => r.UpdatedAtUtc).ToList() : rows.OrderBy(r => r.UpdatedAtUtc).ToList(),
            "min" => desc ? rows.OrderByDescending(r => r.MinimumStock).ToList() : rows.OrderBy(r => r.MinimumStock).ToList(),
            _ => rows
        };
    }

    private static decimal MaterialStockRowLineValue(MaterialStockRow r) =>
        r.UnitPrice.HasValue ? r.Quantity * r.UnitPrice.Value : 0m;

    private static List<MaterialStockGroupedRow> SortMaterialStockGrouped(List<MaterialStockGroupedRow> rows, string col, string dir)
    {
        var desc = dir == "desc";
        return col.ToLowerInvariant() switch
        {
            "name" => desc ? rows.OrderByDescending(r => r.MaterialName).ToList() : rows.OrderBy(r => r.MaterialName).ToList(),
            "qty" => desc ? rows.OrderByDescending(r => r.TotalQuantity).ToList() : rows.OrderBy(r => r.TotalQuantity).ToList(),
            "price" => desc
                ? rows.OrderBy(r => r.UnitPrice == null).ThenByDescending(r => r.UnitPrice).ToList()
                : rows.OrderBy(r => r.UnitPrice == null).ThenBy(r => r.UnitPrice).ToList(),
            "value" => desc
                ? rows.OrderBy(r => r.TotalValue == null).ThenByDescending(r => r.TotalValue).ToList()
                : rows.OrderBy(r => r.TotalValue == null).ThenBy(r => r.TotalValue).ToList(),
            "updated" => desc ? rows.OrderByDescending(r => r.UpdatedAtUtc).ToList() : rows.OrderBy(r => r.UpdatedAtUtc).ToList(),
            "location" => desc ? rows.OrderByDescending(r => r.MaterialName).ToList() : rows.OrderBy(r => r.MaterialName).ToList(),
            "min" => desc ? rows.OrderByDescending(r => r.MinimumStock).ToList() : rows.OrderBy(r => r.MinimumStock).ToList(),
            _ => rows
        };
    }

    private static List<StockSnapshot> SortItemStock(List<StockSnapshot> rows, string col, string dir)
    {
        var desc = dir == "desc";
        return col.ToLowerInvariant() switch
        {
            "sku" => desc
                ? rows.OrderByDescending(r => r.Item?.Sku ?? "").ToList()
                : rows.OrderBy(r => r.Item?.Sku ?? "").ToList(),
            "item" => desc
                ? rows.OrderByDescending(r => r.Item?.Name ?? "").ToList()
                : rows.OrderBy(r => r.Item?.Name ?? "").ToList(),
            "location" => desc
                ? rows.OrderByDescending(r => r.Location?.Code ?? "").ToList()
                : rows.OrderBy(r => r.Location?.Code ?? "").ToList(),
            "qty" => desc ? rows.OrderByDescending(r => r.Quantity).ToList() : rows.OrderBy(r => r.Quantity).ToList(),
            "updated" => desc ? rows.OrderByDescending(r => r.UpdatedAtUtc).ToList() : rows.OrderBy(r => r.UpdatedAtUtc).ToList(),
            "min" => desc
                ? rows.OrderByDescending(r => r.Item?.Material?.MinimumStock ?? 0).ToList()
                : rows.OrderBy(r => r.Item?.Material?.MinimumStock ?? 0).ToList(),
            _ => rows
        };
    }

    public async Task<IActionResult> Movements(
        int pageNumber = 1,
        int pageSize = 10,
        // Shared
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        string? user = null,
        string? notes = null,
        // Material table
        string? matType = null,
        string? matMaterial = null,
        string? matLocation = null,
        string? matDestination = null,
        // Item table
        string? itType = null,
        string? itSku = null,
        string? itItem = null,
        string? itFromLocation = null,
        string? itToLocation = null)
    {
        var matQuery = _db.MaterialStockMovements
            .Include(m => m.Material)
            .Include(m => m.Location)
            .Include(m => m.BuildOrder)
            .AsQueryable();

        if (dateFrom.HasValue)
            matQuery = matQuery.Where(m => m.TimestampUtc >= dateFrom.Value.ToUniversalTime());
        if (dateTo.HasValue)
        {
            var end = dateTo.Value.Date.AddDays(1).ToUniversalTime();
            matQuery = matQuery.Where(m => m.TimestampUtc < end);
        }
        if (!string.IsNullOrWhiteSpace(user))
            matQuery = matQuery.Where(m => (m.PerformedBy ?? "").Contains(user));
        if (!string.IsNullOrWhiteSpace(notes))
            matQuery = matQuery.Where(m => (m.Notes ?? "").Contains(notes));
        if (!string.IsNullOrWhiteSpace(matType))
            matQuery = matQuery.Where(m => m.Type == matType);
        if (!string.IsNullOrWhiteSpace(matMaterial))
            matQuery = matQuery.Where(m => m.Material != null && m.Material.Name.Contains(matMaterial));
        if (!string.IsNullOrWhiteSpace(matLocation))
            matQuery = matQuery.Where(m => m.Location != null && (m.Location.Code ?? "").Contains(matLocation));
        if (!string.IsNullOrWhiteSpace(matDestination))
        {
            matQuery = matQuery.Where(m =>
                (m.BuildOrderId.HasValue && m.BuildOrder != null && (m.BuildOrder.SerialNumber ?? "").Contains(matDestination)) ||
                ((m.DestinationClient ?? "").Contains(matDestination)));
        }

        matQuery = matQuery.OrderByDescending(m => m.TimestampUtc);
        var matTotal = await matQuery.CountAsync();
        var materialMovements = await matQuery.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();

        var itemQuery = _db.Movements
            .Include(m => m.Item)
            .Include(m => m.FromLocation)
            .Include(m => m.ToLocation)
            .AsQueryable();

        if (dateFrom.HasValue)
            itemQuery = itemQuery.Where(m => m.TimestampUtc >= dateFrom.Value.ToUniversalTime());
        if (dateTo.HasValue)
        {
            var end = dateTo.Value.Date.AddDays(1).ToUniversalTime();
            itemQuery = itemQuery.Where(m => m.TimestampUtc < end);
        }
        if (!string.IsNullOrWhiteSpace(user))
            itemQuery = itemQuery.Where(m => (m.PerformedBy ?? "").Contains(user));
        if (!string.IsNullOrWhiteSpace(notes))
            itemQuery = itemQuery.Where(m => (m.Notes ?? "").Contains(notes));
        if (!string.IsNullOrWhiteSpace(itType))
            itemQuery = itemQuery.Where(m => m.Type == itType);
        if (!string.IsNullOrWhiteSpace(itSku))
            itemQuery = itemQuery.Where(m => m.Item != null && (m.Item.Sku ?? "").Contains(itSku));
        if (!string.IsNullOrWhiteSpace(itItem))
            itemQuery = itemQuery.Where(m => m.Item != null && (m.Item.Name ?? "").Contains(itItem));
        if (!string.IsNullOrWhiteSpace(itFromLocation))
            itemQuery = itemQuery.Where(m => m.FromLocation != null && (m.FromLocation.Code ?? "").Contains(itFromLocation));
        if (!string.IsNullOrWhiteSpace(itToLocation))
            itemQuery = itemQuery.Where(m => m.ToLocation != null && (m.ToLocation.Code ?? "").Contains(itToLocation));

        itemQuery = itemQuery.OrderByDescending(m => m.TimestampUtc);
        var itemTotal = await itemQuery.CountAsync();
        var itemMovements = await itemQuery.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();

        ViewBag.Pagination = new Models.PaginationInfo { PageNumber = pageNumber, PageSize = pageSize, TotalItems = Math.Max(matTotal, itemTotal) };
        ViewBag.DateFrom = dateFrom;
        ViewBag.DateTo = dateTo;
        ViewBag.User = user;
        ViewBag.Notes = notes;
        ViewBag.MatType = matType;
        ViewBag.MatMaterial = matMaterial;
        ViewBag.MatLocation = matLocation;
        ViewBag.MatDestination = matDestination;
        ViewBag.ItType = itType;
        ViewBag.ItSku = itSku;
        ViewBag.ItItem = itItem;
        ViewBag.ItFromLocation = itFromLocation;
        ViewBag.ItToLocation = itToLocation;
        var model = new MovementsViewModel
        {
            ItemMovements = itemMovements,
            MaterialMovements = materialMovements
        };
        return View(model);
    }

    public async Task<IActionResult> Reports()
    {
        var materialStockValue = await _db.MaterialStocks
            .Include(ms => ms.Material)
            .Where(ms => ms.Quantity > 0 && ms.Material != null && ms.Material.Price.HasValue)
            .SumAsync(ms => ms.Quantity * ms.Material!.Price!.Value);
        var materialTotalUnits = await _db.MaterialStocks.Where(ms => ms.Quantity > 0).SumAsync(ms => ms.Quantity);
        ViewBag.MaterialStockValue = materialStockValue;
        ViewBag.MaterialTotalUnits = materialTotalUnits;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMaterialMovement(int id)
    {
        var movement = await _db.MaterialStockMovements.FindAsync(id);
        if (movement != null)
        {
            _db.MaterialStockMovements.Remove(movement);
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Movements));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteItemMovement(int id)
    {
        var movement = await _db.Movements.FindAsync(id);
        if (movement != null)
        {
            _db.Movements.Remove(movement);
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Movements));
    }

    /// <summary>Report: stock grouped by warehouse (location).</summary>
    public async Task<IActionResult> ReportByWarehouse(int pageNumber = 1, int pageSize = 10)
    {
        var data = await _db.StockSnapshots
            .Include(s => s.Item)
            .Include(s => s.Location)
            .Where(s => s.Quantity > 0)
            .ToListAsync();
        var byLocation = data
            .GroupBy(s => s.Location?.Code ?? "?")
            .Select(g => new ReportByWarehouseRow
            {
                LocationCode = g.Key,
                LocationName = g.First().Location?.Code ?? g.Key,
                Zone = g.First().Location?.Zone ?? "",
                TotalQuantity = g.Sum(s => s.Quantity),
                LineCount = g.Count(),
                Items = g.Select(s => new ReportStockLine { Sku = s.Item?.Sku ?? "", Name = s.Item?.Name ?? "", Quantity = s.Quantity }).ToList()
            })
            .OrderBy(r => r.LocationCode)
            .ToList();
        ViewBag.Pagination = new Models.PaginationInfo { PageNumber = pageNumber, PageSize = pageSize, TotalItems = byLocation.Count };
        return View(byLocation.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList());
    }

    /// <summary>Report: stock grouped by item.</summary>
    public async Task<IActionResult> ReportByItem(int pageNumber = 1, int pageSize = 10)
    {
        var data = await _db.StockSnapshots
            .Include(s => s.Item)
            .Include(s => s.Location)
            .Where(s => s.Quantity > 0)
            .ToListAsync();
        var byItem = data
            .GroupBy(s => s.ItemId)
            .Select(g =>
            {
                var first = g.First();
                return new ReportByItemRow
                {
                    ItemId = first.ItemId,
                    Sku = first.Item?.Sku ?? "",
                    Name = first.Item?.Name ?? "",
                    ModelOrType = first.Item?.ModelOrType ?? "",
                    TotalQuantity = g.Sum(s => s.Quantity),
                    Locations = g.Select(s => new ReportLocationLine { LocationCode = s.Location?.Code ?? "?", Quantity = s.Quantity }).ToList()
                };
            })
            .OrderBy(r => r.Sku)
            .ToList();
        ViewBag.Pagination = new Models.PaginationInfo { PageNumber = pageNumber, PageSize = pageSize, TotalItems = byItem.Count };
        return View(byItem.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList());
    }

    /// <summary>Report: stock grouped by item model/type.</summary>
    public async Task<IActionResult> ReportByModel(int pageNumber = 1, int pageSize = 10)
    {
        var data = await _db.StockSnapshots
            .Include(s => s.Item)
            .Include(s => s.Location)
            .Where(s => s.Quantity > 0)
            .ToListAsync();
        var byModel = data
            .GroupBy(s => string.IsNullOrWhiteSpace(s.Item?.ModelOrType) ? "(no type)" : s.Item!.ModelOrType)
            .Select(g => new ReportByModelRow
            {
                ModelOrType = g.Key,
                TotalQuantity = g.Sum(s => s.Quantity),
                ItemCount = g.Select(s => s.ItemId).Distinct().Count(),
                Lines = g.GroupBy(x => x.ItemId).Select(ig =>
                {
                    var first = ig.First();
                    return new ReportByModelItemLine { Sku = first.Item?.Sku ?? "", Name = first.Item?.Name ?? "", TotalQty = ig.Sum(s => s.Quantity) };
                }).OrderBy(l => l.Sku).ToList()
            })
            .OrderBy(r => r.ModelOrType)
            .ToList();
        ViewBag.Pagination = new Models.PaginationInfo { PageNumber = pageNumber, PageSize = pageSize, TotalItems = byModel.Count };
        return View(byModel.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList());
    }

    /// <summary>Material stock grouped by location: what material and how much in each location.</summary>
    public async Task<IActionResult> StockByLocation(int pageNumber = 1, int pageSize = 10)
    {
        var materialStock = await _db.MaterialStocks
            .Include(ms => ms.Material)
            .Include(ms => ms.Location)
            .Where(ms => ms.Quantity > 0)
            .OrderBy(ms => ms.Location!.Code)
            .ThenBy(ms => ms.Material!.Name)
            .ToListAsync();

        var byLocation = materialStock
            .GroupBy(ms => new { ms.LocationId, LocationCode = ms.Location?.Code ?? "?", Zone = ms.Location?.Zone ?? "" })
            .Select(g => new StockByLocationRow
            {
                LocationCode = g.Key.LocationCode,
                Zone = g.Key.Zone,
                Materials = g.Select(ms => new MaterialQtyRow
                {
                    MaterialId = ms.MaterialId,
                    LocationId = ms.LocationId,
                    MaterialName = ms.Material?.Name ?? "",
                    Quantity = ms.Quantity,
                    UpdatedAtUtc = ms.UpdatedAtUtc
                }).OrderBy(m => m.MaterialName).ToList()
            })
            .OrderBy(r => r.LocationCode)
            .ToList();

        ViewBag.Pagination = new Models.PaginationInfo { PageNumber = pageNumber, PageSize = pageSize, TotalItems = byLocation.Count };
        return View(byLocation.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList());
    }
}

public class StockByLocationRow
{
    public string LocationCode { get; set; } = "";
    public string Zone { get; set; } = "";
    public List<MaterialQtyRow> Materials { get; set; } = new();
}

public class MaterialQtyRow
{
    public int MaterialId { get; set; }
    public int LocationId { get; set; }
    public string MaterialName { get; set; } = "";
    public int Quantity { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class ReportByWarehouseRow
{
    public string LocationCode { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string Zone { get; set; } = "";
    public int TotalQuantity { get; set; }
    public int LineCount { get; set; }
    public List<ReportStockLine> Items { get; set; } = new();
}

public class ReportStockLine
{
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
}

public class ReportByItemRow
{
    public int ItemId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string ModelOrType { get; set; } = "";
    public int TotalQuantity { get; set; }
    public List<ReportLocationLine> Locations { get; set; } = new();
}

public class ReportLocationLine
{
    public string LocationCode { get; set; } = "";
    public int Quantity { get; set; }
}

public class ReportByModelRow
{
    public string ModelOrType { get; set; } = "";
    public int TotalQuantity { get; set; }
    public int ItemCount { get; set; }
    public List<ReportByModelItemLine> Lines { get; set; } = new();
}

public class ReportByModelItemLine
{
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public int TotalQty { get; set; }
}

public class ReceiveViewModel
{
    public int MaterialId { get; set; }
    public int Quantity { get; set; }
    public decimal? Price { get; set; }
    public int LocationId { get; set; }
}

public class MaterialOutViewModel
{
    public int MaterialId { get; set; }
    public int? LocationId { get; set; }
    public int Quantity { get; set; }
    /// <summary>When set, use this material set instead of single material (expands to multiple outputs).</summary>
    public int? MaterialSetId { get; set; }
    /// <summary>"Order" or "Client"</summary>
    public string DestinationKind { get; set; } = "Client";
    public int? BuildOrderId { get; set; }
    public string? DestinationClient { get; set; }
    public string? Notes { get; set; }
}

public class StockViewModel
{
    public List<StockSnapshot> ItemStock { get; set; } = new();
    public List<MaterialStockRow> MaterialStock { get; set; } = new();
    public List<MaterialStockGroupedRow> MaterialStockGrouped { get; set; } = new();
    public bool ViewByLocation { get; set; }
    public string? MaterialSearch { get; set; }
    /// <summary>Sort column for materials table: name, location, qty, min, price, value, updated.</summary>
    public string? MaterialSort { get; set; }
    public string MaterialSortDir { get; set; } = "asc";
    /// <summary>Sort column for items table: sku, item, location, qty, min, updated.</summary>
    public string? ItemSort { get; set; }
    public string ItemSortDir { get; set; } = "asc";

    public string NextMaterialSortDir(string column) =>
        string.Equals(MaterialSort, column, StringComparison.OrdinalIgnoreCase)
        && string.Equals(MaterialSortDir, "asc", StringComparison.OrdinalIgnoreCase)
            ? "desc"
            : "asc";

    public string NextItemSortDir(string column) =>
        string.Equals(ItemSort, column, StringComparison.OrdinalIgnoreCase)
        && string.Equals(ItemSortDir, "asc", StringComparison.OrdinalIgnoreCase)
            ? "desc"
            : "asc";

    public string MaterialSortCaret(string column) =>
        !string.Equals(MaterialSort, column, StringComparison.OrdinalIgnoreCase)
            ? ""
            : (string.Equals(MaterialSortDir, "desc", StringComparison.OrdinalIgnoreCase) ? " \u25bc" : " \u25b2");

    public string ItemSortCaret(string column) =>
        !string.Equals(ItemSort, column, StringComparison.OrdinalIgnoreCase)
            ? ""
            : (string.Equals(ItemSortDir, "desc", StringComparison.OrdinalIgnoreCase) ? " \u25bc" : " \u25b2");
}

public class MaterialStockRow
{
    public int MaterialId { get; set; }
    public int LocationId { get; set; }
    public string MaterialName { get; set; } = "";
    public string LocationCode { get; set; } = "";
    public int Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public int MinimumStock { get; set; }
    /// <summary>Total stock across all locations for this material (for below-minimum alert).</summary>
    public int TotalQuantityForMaterial { get; set; }
}

public class MaterialStockGroupedRow
{
    public int MaterialId { get; set; }
    public string MaterialName { get; set; } = "";
    public int TotalQuantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? TotalValue { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public int MinimumStock { get; set; }
}

public class MovementsViewModel
{
    public List<Movement> ItemMovements { get; set; } = new();
    public List<MaterialStockMovement> MaterialMovements { get; set; } = new();
}
