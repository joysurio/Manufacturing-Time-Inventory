using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Execution;
using ManufacturingTimeTracking.Models.Catalog;
using ManufacturingTimeTracking.Models.Inventory;
using ManufacturingTimeTracking.Models.Templates;
using System.Security.Claims;

namespace ManufacturingTimeTracking.Pages.Orders;

public class PickListModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public PickListModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public BuildOrder Order { get; set; } = null!;
    public string MachineModelName { get; set; } = "";
    public List<PickListLine> Lines { get; set; } = new();
    /// <summary>Lines filtered by picking status: All, Pending, PartiallyPicked, FullyPicked.</summary>
    public List<PickListLine> FilteredLines { get; set; } = new();
    /// <summary>Current filter from query: All, Pending, PartiallyPicked, FullyPicked.</summary>
    public string PickingFilter { get; set; } = "All";
    public List<MaterialSetSummary> MaterialSets { get; set; } = new();
    public int OrderId { get; set; }

    private async Task<string> GetPerformedByAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            // Use the stable userId -> email mapping; avoids issues where Identity.Name is wrong.
            var email = await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(email))
                return email;
        }

        return User.Identity?.Name ?? "";
    }

    private async Task<bool> DeductMaterialInventoryAsync(int buildOrderId, int materialId, int quantityToDeduct)
    {
        if (quantityToDeduct <= 0) return true;

        var totalAvailable = await _context.MaterialStocks
            .Where(ms => ms.MaterialId == materialId)
            .SumAsync(ms => ms.Quantity);
        if (totalAvailable < quantityToDeduct) return false;

        var performedBy = await GetPerformedByAsync();
        var remaining = quantityToDeduct;

        var stocks = await _context.MaterialStocks
            .Include(ms => ms.Location)
            .Where(ms => ms.MaterialId == materialId && ms.Quantity > 0)
            .OrderBy(ms => ms.Location != null ? ms.Location.Zone : "")
            .ThenBy(ms => ms.Location != null ? ms.Location.Code : "")
            .ToListAsync();

        foreach (var stock in stocks)
        {
            if (remaining <= 0) break;

            var take = Math.Min(stock.Quantity, remaining);
            if (take <= 0) continue;

            var now = DateTime.UtcNow;
            stock.Quantity -= take;
            stock.UpdatedAtUtc = now;

            _context.MaterialStockMovements.Add(new MaterialStockMovement
            {
                MaterialId = materialId,
                LocationId = stock.LocationId,
                QuantityDelta = -take,
                Type = "OUT",
                Notes = "Pick list (material set)",
                PerformedBy = performedBy,
                TimestampUtc = now,
                BuildOrderId = buildOrderId
            });

            remaining -= take;
        }

        return remaining == 0;
    }

    private async Task<OrderMaterialPickLine?> FindExistingMaterialPickLineAsync(int buildOrderId, int materialId)
    {
        // Prefer model/variant component lines so that when a material is already visible in the list,
        // adding a material set marks the existing row (instead of creating a duplicate ad-hoc line).
        var componentLine = await _context.OrderMaterialPickLines
            .FirstOrDefaultAsync(l =>
                l.BuildOrderId == buildOrderId &&
                l.MaterialId == materialId &&
                (l.ComponentId != null || l.VariantComponentId != null));

        if (componentLine != null) return componentLine;

        // Otherwise fall back to ad-hoc lines (previous material-set additions).
        return await _context.OrderMaterialPickLines
            .FirstOrDefaultAsync(l =>
                l.BuildOrderId == buildOrderId &&
                l.MaterialId == materialId &&
                l.ComponentId == null &&
                l.VariantComponentId == null);
    }

    private async Task<(int Needed, int Picked, int? ModelComponentId, int? VariantComponentId)>
        GetMergedMaterialComponentTotalsAsync(int buildOrderId, int materialId)
    {
        // Only material-only components (ItemId == null) are merged between model and variant.
        var order = await _context.BuildOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == buildOrderId);

        if (order == null)
            return (0, 0, null, null);

        var modelComponents = await _context.MachineModelComponents
            .AsNoTracking()
            .Where(c =>
                c.MachineModelId == order.MachineModelId &&
                c.MaterialId == materialId &&
                c.ItemId == null)
            .Select(c => new { c.Id, c.Quantity })
            .ToListAsync();

        var variantComponents = await _context.MachineVariantComponents
            .AsNoTracking()
            .Where(c =>
                c.MachineVariantId == order.MachineVariantId &&
                c.MaterialId == materialId &&
                c.ItemId == null)
            .Select(c => new { c.Id, c.Quantity })
            .ToListAsync();

        var needed = modelComponents.Sum(c => c.Quantity) + variantComponents.Sum(c => c.Quantity);

        var modelIds = modelComponents.Select(c => c.Id).ToList();
        var variantIds = variantComponents.Select(c => c.Id).ToList();

        var picked = await _context.OrderMaterialPickLines
            .AsNoTracking()
            .Where(l => l.BuildOrderId == buildOrderId && l.MaterialId == materialId)
            .Where(l =>
                (l.ComponentId.HasValue && modelIds.Contains(l.ComponentId.Value)) ||
                (l.VariantComponentId.HasValue && variantIds.Contains(l.VariantComponentId.Value)))
            .SumAsync(l => (int?)l.QuantityPicked) ?? 0;

        // Prefer model component as representative (for UI behavior like ComponentId hidden fields).
        var modelComponentId = modelComponents.Count > 0 ? modelComponents[0].Id : (int?)null;
        var variantComponentId = modelComponentId == null && variantComponents.Count > 0 ? variantComponents[0].Id : (int?)null;

        return (needed, picked, modelComponentId, variantComponentId);
    }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null) return NotFound();

        var order = await _context.BuildOrders.FindAsync(id);
        if (order == null) return NotFound();
        Order = order;
        OrderId = order.Id;

        var model = await _context.MachineModels
            .Include(m => m.Components)
                .ThenInclude(c => c.Item)
            .Include(m => m.Components)
                .ThenInclude(c => c.Material)
            .Include(m => m.Components)
                .ThenInclude(c => c.Alternatives)
                    .ThenInclude(a => a.Item)
            .FirstOrDefaultAsync(m => m.Id == order.MachineModelId);
        var variant = await _context.MachineVariants
            .Include(v => v.Components)
                .ThenInclude(c => c.Item)
            .Include(v => v.Components)
                .ThenInclude(c => c.Material)
            .Include(v => v.Components)
                .ThenInclude(c => c.Alternatives)
                    .ThenInclude(a => a.Item)
            .FirstOrDefaultAsync(v => v.Id == order.MachineVariantId);
        if (model == null)
        {
            MachineModelName = $"Model {order.MachineModelId}";
            await LoadLinesFromPickLinesAsync(order.Id);
            ApplyPickingFilter();
            return Page();
        }

        MachineModelName = model.Name;

        // Ensure OrderPickLine rows exist from model components (items + materials resolved to items)
        var existing = await _context.OrderPickLines
            .Where(l => l.BuildOrderId == order.Id)
            .Select(l => l.ItemId)
            .ToListAsync();

        async Task EnsurePickLineAsync(int? itemId, int? materialId, int quantity, IEnumerable<int> alternativeItemIds)
        {
            var itemIdsToAdd = new List<int>();
            if (itemId.HasValue)
            {
                itemIdsToAdd.Add(itemId.Value);
                itemIdsToAdd.AddRange(alternativeItemIds);
            }
            else if (materialId.HasValue)
            {
                var itemIdForMaterial = await _context.Items
                    .Where(i => i.MaterialId == materialId.Value)
                    .Select(i => (int?)i.Id)
                    .FirstOrDefaultAsync();
                if (itemIdForMaterial.HasValue)
                    itemIdsToAdd.Add(itemIdForMaterial.Value);
            }
            foreach (var itemIdToAdd in itemIdsToAdd.Distinct())
            {
                if (existing.Contains(itemIdToAdd)) continue;
                _context.OrderPickLines.Add(new OrderPickLine
                {
                    BuildOrderId = order.Id,
                    ItemId = itemIdToAdd,
                    QuantityRequired = quantity,
                    QuantityPicked = 0
                });
                existing.Add(itemIdToAdd);
            }
        }

        foreach (var c in model.Components ?? new List<MachineModelComponent>())
            await EnsurePickLineAsync(c.ItemId, c.MaterialId, c.Quantity, c.Alternatives.Select(a => a.ItemId));
        foreach (var c in variant?.Components ?? new List<MachineVariantComponent>())
            await EnsurePickLineAsync(c.ItemId, c.MaterialId, c.Quantity, c.Alternatives.Select(a => a.ItemId));

        await _context.SaveChangesAsync();

        MaterialSets = await _context.MaterialSets
            .Include(s => s.Items)
            .OrderBy(s => s.Name)
            .Select(s => new MaterialSetSummary { Id = s.Id, Name = s.Name, ItemCount = s.Items.Count })
            .ToListAsync();
        await LoadLinesFromPickLinesAsync(order.Id);
        ApplyPickingFilter();
        return Page();
    }

    private void ApplyPickingFilter()
    {
        var filter = (Request.Query["filter"].FirstOrDefault() ?? "All").Trim();
        if (filter != "Pending" && filter != "PartiallyPicked" && filter != "FullyPicked")
            filter = "All";
        PickingFilter = filter;
        FilteredLines = filter switch
        {
            "Pending" => Lines.Where(l => l.QuantityPicked <= 0).ToList(),
            "PartiallyPicked" => Lines.Where(l => l.QuantityPicked > 0 && l.QuantityPicked < l.QuantityNeeded).ToList(),
            "FullyPicked" => Lines.Where(l => l.QuantityPicked >= l.QuantityNeeded).ToList(),
            _ => Lines.ToList()
        };
    }

    private async Task LoadLinesFromPickLinesAsync(int buildOrderId)
    {
        // Get order and model to understand component groups
        var order = await _context.BuildOrders.FindAsync(buildOrderId);
        if (order == null) return;

        var model = await _context.MachineModels
            .Include(m => m.Components)
                .ThenInclude(c => c.Item)
            .Include(m => m.Components)
                .ThenInclude(c => c.Material)
            .Include(m => m.Components)
                .ThenInclude(c => c.Alternatives)
                    .ThenInclude(a => a.Item)
            .FirstOrDefaultAsync(m => m.Id == order.MachineModelId);
        var variant = await _context.MachineVariants
            .Include(v => v.Components)
                .ThenInclude(c => c.Item)
            .Include(v => v.Components)
                .ThenInclude(c => c.Material)
            .Include(v => v.Components)
                .ThenInclude(c => c.Alternatives)
                    .ThenInclude(a => a.Item)
            .FirstOrDefaultAsync(v => v.Id == order.MachineVariantId);

        var pickLines = await _context.OrderPickLines
            .Include(l => l.Item)
            .Where(l => l.BuildOrderId == buildOrderId)
            .ToListAsync();

        var materialPickLines = await _context.OrderMaterialPickLines
            .Where(l => l.BuildOrderId == buildOrderId)
            .ToListAsync();

        var itemIds = pickLines.Select(l => l.ItemId).Distinct().ToList();
        var stockByItem = await _context.StockSnapshots
            .Include(s => s.Location)
            .Where(s => itemIds.Contains(s.ItemId) && s.Quantity > 0)
            .ToListAsync();

        var lines = new List<PickListLine>();
        
        if (model != null)
        {
            var combinedItemComponents = (model.Components ?? new List<MachineModelComponent>())
                .Where(c => c.ItemId.HasValue)
                .Select(c => (source: "model", model: c, variant: (MachineVariantComponent?)null))
                .Concat((variant?.Components ?? new List<MachineVariantComponent>())
                    .Where(c => c.ItemId.HasValue)
                    .Select(c => (source: "variant", model: (MachineModelComponent?)null, variant: c)))
                .ToList();

            // Merge model + variant item components when they represent the same thing.
            // Preferred key: linked MaterialId (same material behind different/legacy item rows).
            // Fallback key: primary ItemId.
            foreach (var componentGroup in combinedItemComponents.GroupBy(c =>
            {
                if (c.source == "model")
                {
                    // Prefer explicit component MaterialId when available.
                    if (c.model!.MaterialId.HasValue) return $"mat:{c.model.MaterialId.Value}";
                    if (c.model.Item?.MaterialId.HasValue == true) return $"mat:{c.model.Item.MaterialId.Value}";
                    return $"item:{c.model.ItemId!.Value}";
                }

                if (c.variant!.MaterialId.HasValue) return $"mat:{c.variant.MaterialId.Value}";
                if (c.variant.Item?.MaterialId.HasValue == true) return $"mat:{c.variant.Item.MaterialId.Value}";
                return $"item:{c.variant.ItemId!.Value}";
            }))
            {
                var wraps = componentGroup.ToList();
                var firstWrap = wraps[0];
                var firstItem = firstWrap.source == "model" ? firstWrap.model!.Item : firstWrap.variant!.Item;
                if (firstItem == null) continue;

                var primaryItemId = firstWrap.source == "model" ? firstWrap.model!.ItemId!.Value : firstWrap.variant!.ItemId!.Value;
                var quantityNeeded = wraps.Sum(w => w.source == "model" ? w.model!.Quantity : w.variant!.Quantity);

                var allItemIds = wraps
                    .SelectMany(w =>
                    {
                        if (w.source == "model")
                            return new[] { w.model!.ItemId!.Value }.Concat(w.model.Alternatives.Select(a => a.ItemId));
                        return new[] { w.variant!.ItemId!.Value }.Concat(w.variant.Alternatives.Select(a => a.ItemId));
                    })
                    .Distinct()
                    .ToList();

                var componentPickLines = pickLines.Where(pl => allItemIds.Contains(pl.ItemId)).ToList();
                var totalPicked = componentPickLines.Sum(pl => pl.QuantityPicked);

                var primaryStock = stockByItem
                    .Where(s => s.ItemId == primaryItemId)
                    .OrderBy(s => s.Location?.Zone)
                    .ThenBy(s => s.Location?.Code)
                    .ToList();

                var alternativeItemIds = wraps
                    .SelectMany(w => w.source == "model"
                        ? w.model!.Alternatives.Select(a => a.ItemId)
                        : w.variant!.Alternatives.Select(a => a.ItemId))
                    .Distinct()
                    .ToList();

                var alternativeItems = new List<PickListAlternative>();
                foreach (var altItemId in alternativeItemIds)
                {
                    var altItem = wraps
                        .SelectMany(w => w.source == "model"
                            ? w.model!.Alternatives.Select(a => a.Item)
                            : w.variant!.Alternatives.Select(a => a.Item))
                        .FirstOrDefault(i => i.Id == altItemId);
                    if (altItem == null) continue;

                    var altPl = componentPickLines.FirstOrDefault(pl => pl.ItemId == altItemId);
                    var altStock = stockByItem.Where(s => s.ItemId == altItemId).OrderBy(s => s.Location?.Zone).ThenBy(s => s.Location?.Code).ToList();
                    alternativeItems.Add(new PickListAlternative
                    {
                        ItemId = altItemId,
                        Sku = altItem.Sku,
                        Name = altItem.Name,
                        ModelOrType = altItem.ModelOrType ?? "",
                        QuantityPicked = altPl?.QuantityPicked ?? 0,
                        TotalAvailable = altStock.Sum(s => s.Quantity),
                        Locations = altStock.Select(s => new PickListLocation { Code = s.Location?.Code ?? "?", Zone = s.Location?.Zone ?? "", Quantity = s.Quantity }).ToList()
                    });
                }

                var notes = wraps
                    .Select(w => w.source == "model" ? w.model!.Notes : w.variant!.Notes)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .ToList();

                var modelRep = wraps.FirstOrDefault(w => w.source == "model");
                var variantRep = wraps.FirstOrDefault(w => w.source == "variant");

                lines.Add(new PickListLine
                {
                    ComponentId = modelRep.model?.Id,
                    VariantComponentId = modelRep.model == null ? variantRep.variant?.Id : null,
                    ItemId = primaryItemId,
                    Sku = firstItem.Sku,
                    Name = firstItem.Name,
                    ModelOrType = firstItem.ModelOrType ?? "",
                    QuantityNeeded = quantityNeeded,
                    QuantityPicked = totalPicked,
                    Notes = notes.Any() ? string.Join(" | ", notes) : null,
                    Locations = primaryStock.Select(s => new PickListLocation { Code = s.Location?.Code ?? "?", Zone = s.Location?.Zone ?? "", Quantity = s.Quantity }).ToList(),
                    TotalAvailable = primaryStock.Sum(s => s.Quantity) + alternativeItems.Sum(a => a.TotalAvailable),
                    Alternatives = alternativeItems
                });
            }
            // Include pick lines from Material-based components (resolved to item when building OrderPickLines)
            var itemIdsInLines = new HashSet<int>(lines.Select(l => l.ItemId).Concat(lines.SelectMany(l => l.Alternatives.Select(a => a.ItemId))));
            foreach (var pl in pickLines.Where(pl => !itemIdsInLines.Contains(pl.ItemId)))
            {
                itemIdsInLines.Add(pl.ItemId);
                var stock = stockByItem.Where(s => s.ItemId == pl.ItemId).OrderBy(s => s.Location?.Zone).ThenBy(s => s.Location?.Code).ToList();
                lines.Add(new PickListLine
                {
                    ItemId = pl.ItemId,
                    Sku = pl.Item?.Sku ?? "",
                    Name = pl.Item?.Name ?? "",
                    ModelOrType = pl.Item?.ModelOrType ?? "",
                    QuantityNeeded = pl.QuantityRequired,
                    QuantityPicked = pl.QuantityPicked,
                    Notes = null,
                    Locations = stock.Select(s => new PickListLocation { Code = s.Location?.Code ?? "?", Zone = s.Location?.Zone ?? "", Quantity = s.Quantity }).ToList(),
                    TotalAvailable = stock.Sum(s => s.Quantity)
                });
            }
            // Show Material-only components that have no linked Item (manual pick via combo box)
            var combinedMaterialOnlyComponents = (model.Components ?? new List<MachineModelComponent>())
                .Where(c => c.MaterialId.HasValue && !c.ItemId.HasValue)
                .Select(c => (source: "model", model: c, variant: (MachineVariantComponent?)null))
                .Concat((variant?.Components ?? new List<MachineVariantComponent>())
                    .Where(c => c.MaterialId.HasValue && !c.ItemId.HasValue)
                    .Select(c => (source: "variant", model: (MachineModelComponent?)null, variant: c)))
                .ToList();

            // Merge identical materials across model-level and variant-level component definitions
            // (so the same MaterialId is shown only once in the pick list).
            foreach (var materialGroup in combinedMaterialOnlyComponents.GroupBy(c =>
                         c.source == "model" ? c.model!.MaterialId!.Value : c.variant!.MaterialId!.Value))
            {
                var materialId = materialGroup.Key;

                // If there is a linked inventory item for this material, it is already shown via OrderPickLine.
                var linkedItemId = await _context.Items
                    .Where(i => i.MaterialId == materialId)
                    .Select(i => (int?)i.Id)
                    .FirstOrDefaultAsync();
                if (linkedItemId.HasValue)
                    continue;

                var firstWrap = materialGroup.First();
                var materialEntity = firstWrap.source == "model" ? firstWrap.model!.Material : firstWrap.variant!.Material;

                var totalQuantityNeeded = materialGroup.Sum(w => w.source == "model" ? w.model!.Quantity : w.variant!.Quantity);

                var totalQuantityPicked = 0;
                OrderMaterialPickLine? representativePickLine = null;
                foreach (var wrap in materialGroup)
                {
                    var pickLine = wrap.source == "model"
                        ? materialPickLines.FirstOrDefault(l => l.ComponentId == wrap.model!.Id)
                        : materialPickLines.FirstOrDefault(l => l.VariantComponentId == wrap.variant!.Id);

                    if (pickLine != null)
                    {
                        totalQuantityPicked += pickLine.QuantityPicked;

                        // Prefer the line with most picked so the '-' button is available when needed.
                        if (representativePickLine == null || pickLine.QuantityPicked > representativePickLine.QuantityPicked)
                            representativePickLine = pickLine;
                    }
                }

                // For UI actions when the pick line doesn't exist yet: prefer model componentId.
                var modelComponent = materialGroup.FirstOrDefault(w => w.source == "model");
                var modelComponentId = modelComponent.model?.Id;

                var notes = materialGroup
                    .Select(w => w.source == "model" ? w.model!.Notes : w.variant!.Notes)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .ToList();

                lines.Add(new PickListLine
                {
                    ComponentId = modelComponentId,
                    VariantComponentId = modelComponentId == null ? materialGroup.FirstOrDefault(w => w.source == "variant").variant?.Id : null,
                    OrderMaterialPickLineId = representativePickLine?.Id,
                    ItemId = 0,
                    MaterialId = materialId,
                    MaterialName = materialEntity?.Name ?? "Material",
                    MaterialReference = materialEntity?.Reference,
                    Sku = "—",
                    Name = materialEntity?.Name ?? "Material",
                    ModelOrType = "",
                    QuantityNeeded = totalQuantityNeeded,
                    QuantityPicked = totalQuantityPicked,
                    Notes = notes.Any() ? string.Join(" | ", notes) : null,
                    TotalAvailable = 0,
                    Locations = new List<PickListLocation>(),
                    Alternatives = new List<PickListAlternative>()
                });
            }
            // Ad-hoc material lines (from material sets, no component reference)
            var adHocMaterialIds = materialPickLines.Where(l => !l.ComponentId.HasValue && !l.VariantComponentId.HasValue).Select(l => l.MaterialId).Distinct().ToList();
            if (adHocMaterialIds.Any())
            {
                var adHocMaterialData = await _context.Materials
                    .Where(m => adHocMaterialIds.Contains(m.Id))
                    .Select(m => new { m.Id, m.Name, m.Reference })
                    .ToListAsync();
                var adHocNames = adHocMaterialData.ToDictionary(m => m.Id, m => m.Name);
                var adHocRefs = adHocMaterialData.ToDictionary(m => m.Id, m => m.Reference);
                foreach (var matLine in materialPickLines.Where(l => !l.ComponentId.HasValue && !l.VariantComponentId.HasValue))
                {
                    var name = adHocNames.GetValueOrDefault(matLine.MaterialId, "Material");
                    lines.Add(new PickListLine
                    {
                        ComponentId = null,
                        VariantComponentId = null,
                        OrderMaterialPickLineId = matLine.Id,
                        ItemId = 0,
                        MaterialId = matLine.MaterialId,
                        MaterialName = name,
                        MaterialReference = adHocRefs.GetValueOrDefault(matLine.MaterialId),
                        Sku = "—",
                        Name = name,
                        ModelOrType = "",
                        QuantityNeeded = matLine.QuantityRequired,
                        QuantityPicked = matLine.QuantityPicked,
                        Notes = "From material set",
                        TotalAvailable = 0,
                        Locations = new List<PickListLocation>(),
                        Alternatives = new List<PickListAlternative>()
                    });
                }
            }
        }
        else
        {
            // Fallback: show all pick lines as-is (no grouping)
            foreach (var pl in pickLines)
            {
                var stock = stockByItem.Where(s => s.ItemId == pl.ItemId).OrderBy(s => s.Location?.Zone).ThenBy(s => s.Location?.Code).ToList();
                lines.Add(new PickListLine
                {
                    ItemId = pl.ItemId,
                    Sku = pl.Item?.Sku ?? "",
                    Name = pl.Item?.Name ?? "",
                    ModelOrType = pl.Item?.ModelOrType ?? "",
                    QuantityNeeded = pl.QuantityRequired,
                    QuantityPicked = pl.QuantityPicked,
                    Notes = null,
                    Locations = stock.Select(s => new PickListLocation { Code = s.Location?.Code ?? "?", Zone = s.Location?.Zone ?? "", Quantity = s.Quantity }).ToList(),
                    TotalAvailable = stock.Sum(s => s.Quantity)
                });
            }
        }

        // Sort by warehouse position so person can walk shelves: by first location zone then code
        Lines = lines
            .OrderBy(l => l.Locations.FirstOrDefault()?.Zone ?? l.Alternatives.FirstOrDefault()?.Locations.FirstOrDefault()?.Zone ?? "zzz")
            .ThenBy(l => l.Locations.FirstOrDefault()?.Code ?? l.Alternatives.FirstOrDefault()?.Locations.FirstOrDefault()?.Code ?? "zzz")
            .ThenBy(l => l.Sku)
            .ToList();
    }

    /// <summary>Unified search: accept material, material set, or QR code; auto-detect type and add or mark picked.</summary>
    public async Task<IActionResult> OnPostUnifiedSearchAsync(int? id, string? searchValue, string? selectedValue)
    {
        if (id == null)
        {
            TempData["PickListError"] = "Order not specified.";
            return RedirectToPage("./Index");
        }
        // If user picked a suggestion from the dropdown, handle by type.
        if (!string.IsNullOrWhiteSpace(selectedValue))
        {
            // Material set selected (e.g. "set_5") → add materials and mark picked (deduct inventory).
            if (selectedValue.StartsWith("set_") && int.TryParse(selectedValue["set_".Length..], out int selectedSetId) && selectedSetId > 0)
            {
                var materialSet = await _context.MaterialSets
                    .Include(s => s.Items)
                    .ThenInclude(i => i.Material)
                    .FirstOrDefaultAsync(s => s.Id == selectedSetId);
                if (materialSet == null)
                {
                    TempData["PickListError"] = "Material set not found.";
                    return RedirectToPage("./PickList", new { id, filter = Request.Query["filter"].FirstOrDefault() });
                }
                var setItems = materialSet.Items?
                    .Where(i => i.Quantity > 0)
                    .GroupBy(i => i.MaterialId)
                    .Select(g => (MaterialId: g.Key, Quantity: g.Sum(x => x.Quantity)))
                    .ToList() ?? new List<(int MaterialId, int Quantity)>();
                if (!setItems.Any())
                {
                    TempData["PickListError"] = "This material set has no materials (or all quantities are zero). Add materials to the set first.";
                    return RedirectToPage("./PickList", new { id, filter = Request.Query["filter"].FirstOrDefault() });
                }
                var order = await _context.BuildOrders.FindAsync(id);
                if (order == null) return NotFound();
                var noStock = false;
                var strategy = _context.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await _context.Database.BeginTransactionAsync();
                    foreach (var item in setItems)
                    {
                        var totals = await GetMergedMaterialComponentTotalsAsync(id.Value, item.MaterialId);
                        var existingLine = await FindExistingMaterialPickLineAsync(id.Value, item.MaterialId);
                        if (existingLine != null)
                        {
                            var remainingNeeded = totals.Needed > 0
                                ? Math.Max(0, totals.Needed - totals.Picked)
                                : Math.Max(0, existingLine.QuantityRequired - existingLine.QuantityPicked);
                            var pickQty = Math.Min(item.Quantity, remainingNeeded);
                            if (pickQty <= 0) continue;

                            var deducted = await DeductMaterialInventoryAsync(id.Value, item.MaterialId, pickQty);
                            if (!deducted) { noStock = true; break; }

                            existingLine.QuantityPicked += pickQty;
                            if (totals.Needed > 0)
                            {
                                existingLine.QuantityRequired = totals.Needed;
                                if (!existingLine.ComponentId.HasValue && !existingLine.VariantComponentId.HasValue)
                                {
                                    existingLine.ComponentId = totals.ModelComponentId;
                                    existingLine.VariantComponentId = totals.ModelComponentId == null ? totals.VariantComponentId : null;
                                }
                            }
                        }
                        else
                        {
                            var pickQty = totals.Needed > 0
                                ? Math.Min(item.Quantity, Math.Max(0, totals.Needed - totals.Picked))
                                : item.Quantity;

                            if (pickQty <= 0) continue;

                            if (totals.Needed > 0)
                            {
                                _context.OrderMaterialPickLines.Add(new OrderMaterialPickLine
                                {
                                    BuildOrderId = id.Value,
                                    ComponentId = totals.ModelComponentId,
                                    VariantComponentId = totals.ModelComponentId == null ? totals.VariantComponentId : null,
                                    MaterialId = item.MaterialId,
                                    QuantityRequired = totals.Needed,
                                    QuantityPicked = pickQty
                                });
                            }
                            else
                            {
                                _context.OrderMaterialPickLines.Add(new OrderMaterialPickLine
                                {
                                    BuildOrderId = id.Value,
                                    ComponentId = null,
                                    VariantComponentId = null,
                                    MaterialId = item.MaterialId,
                                    QuantityRequired = item.Quantity,
                                    QuantityPicked = pickQty
                                });
                            }

                            var deducted = await DeductMaterialInventoryAsync(id.Value, item.MaterialId, pickQty);
                            if (!deducted) { noStock = true; break; }
                        }
                    }

                    if (noStock)
                    {
                        await tx.RollbackAsync();
                        return;
                    }

                    await _context.SaveChangesAsync();
                    await tx.CommitAsync();
                });
                if (noStock)
                {
                    TempData["PickListError"] = "No stock available for this material set item.";
                    return RedirectToPage("./PickList", new { id, filter = Request.Query["filter"].FirstOrDefault() });
                }
                TempData["PickListSuccess"] = $"Material set \"{materialSet.Name}\" added and picked ({setItems.Count} material(s)).";
                return RedirectToPage("./PickList", new { id, filter = Request.Query["filter"].FirstOrDefault() });
            }
            return await OnPostMarkPickedAsync(id, selectedValue);
        }
        if (string.IsNullOrWhiteSpace(searchValue))
        {
            TempData["PickListError"] = "Enter or scan a material, material set name, or QR code (tag/SKU).";
            return RedirectToPage("./PickList", new { id });
        }

        var value = searchValue.Trim();

        // 1) Material set: by ID (numeric) or by name
        var setById = int.TryParse(value, out var setId) && setId > 0
            ? await _context.MaterialSets.Include(s => s.Items).ThenInclude(i => i.Material).FirstOrDefaultAsync(s => s.Id == setId)
            : null;
        var set = setById ?? await _context.MaterialSets
            .Include(s => s.Items)
            .ThenInclude(i => i.Material)
            .FirstOrDefaultAsync(s => s.Name.ToLower() == value.ToLower())
            ?? await _context.MaterialSets
            .Include(s => s.Items)
            .ThenInclude(i => i.Material)
            .FirstOrDefaultAsync(s => s.Name.Contains(value));

        var setItemsList = set?.Items?.Where(i => i.Quantity > 0).ToList();
        if (set != null && setItemsList != null && setItemsList.Count > 0)
        {
            var order = await _context.BuildOrders.FindAsync(id);
            if (order == null) return NotFound();

            var setItemsGrouped = setItemsList
                .GroupBy(i => i.MaterialId)
                .Select(g => (MaterialId: g.Key, Quantity: g.Sum(x => x.Quantity)))
                .ToList();

            var noStock = false;
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _context.Database.BeginTransactionAsync();
                foreach (var item in setItemsGrouped)
                {
                    var totals = await GetMergedMaterialComponentTotalsAsync(id.Value, item.MaterialId);
                    var existingLine = await FindExistingMaterialPickLineAsync(id.Value, item.MaterialId);
                    if (existingLine != null)
                    {
                        var remainingNeeded = totals.Needed > 0
                            ? Math.Max(0, totals.Needed - totals.Picked)
                            : Math.Max(0, existingLine.QuantityRequired - existingLine.QuantityPicked);
                        var pickQty = Math.Min(item.Quantity, remainingNeeded);
                        if (pickQty <= 0) continue;

                        var deducted = await DeductMaterialInventoryAsync(id.Value, item.MaterialId, pickQty);
                        if (!deducted) { noStock = true; break; }

                        existingLine.QuantityPicked += pickQty;
                        if (totals.Needed > 0)
                        {
                            existingLine.QuantityRequired = totals.Needed;
                            if (!existingLine.ComponentId.HasValue && !existingLine.VariantComponentId.HasValue)
                            {
                                existingLine.ComponentId = totals.ModelComponentId;
                                existingLine.VariantComponentId = totals.ModelComponentId == null ? totals.VariantComponentId : null;
                            }
                        }
                    }
                    else
                    {
                        var pickQty = totals.Needed > 0
                            ? Math.Min(item.Quantity, Math.Max(0, totals.Needed - totals.Picked))
                            : item.Quantity;

                        if (pickQty <= 0) continue;

                        if (totals.Needed > 0)
                        {
                            _context.OrderMaterialPickLines.Add(new OrderMaterialPickLine
                            {
                                BuildOrderId = id.Value,
                                ComponentId = totals.ModelComponentId,
                                VariantComponentId = totals.ModelComponentId == null ? totals.VariantComponentId : null,
                                MaterialId = item.MaterialId,
                                QuantityRequired = totals.Needed,
                                QuantityPicked = pickQty
                            });
                        }
                        else
                        {
                            _context.OrderMaterialPickLines.Add(new OrderMaterialPickLine
                            {
                                BuildOrderId = id.Value,
                                ComponentId = null,
                                VariantComponentId = null,
                                MaterialId = item.MaterialId,
                                QuantityRequired = item.Quantity,
                                QuantityPicked = pickQty
                            });
                        }

                        var deducted = await DeductMaterialInventoryAsync(id.Value, item.MaterialId, pickQty);
                        if (!deducted) { noStock = true; break; }
                    }
                }

                if (noStock)
                {
                    await tx.RollbackAsync();
                    return;
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            });
            if (noStock)
            {
                TempData["PickListError"] = "No stock available for this material set item.";
                return RedirectToPage("./PickList", new { id, filter = Request.Query["filter"].FirstOrDefault() });
            }
            TempData["PickListSuccess"] = $"Material set \"{set.Name}\" added and picked ({setItemsGrouped.Count} material(s)).";
            return RedirectToPage("./PickList", new { id, filter = Request.Query["filter"].FirstOrDefault() });
        }
        if (set != null && (set.Items == null || !set.Items.Any(i => i.Quantity > 0)))
        {
            TempData["PickListError"] = "This material set has no materials (or all quantities are zero). Add materials to the set first.";
            return RedirectToPage("./PickList", new { id, filter = Request.Query["filter"].FirstOrDefault() });
        }

        // 2) QR / Tag / SKU / Material QR → mark picked (or add material if only MaterialId and not on list)
        var itemId = await _context.Tags.Where(t => t.Code == value).Select(t => (int?)t.ItemId).FirstOrDefaultAsync();
        if (itemId == null)
            itemId = await _context.Items.Where(i => i.Sku == value).Select(i => (int?)i.Id).FirstOrDefaultAsync();

        int? materialIdFromCode = null;
        if (itemId == null)
        {
            materialIdFromCode = await _context.Materials.Where(m => m.Qr == value).Select(m => (int?)m.Id).FirstOrDefaultAsync();
            if (materialIdFromCode.HasValue)
            {
                itemId = await _context.Items.Where(i => i.MaterialId == materialIdFromCode.Value).Select(i => (int?)i.Id).FirstOrDefaultAsync();
            }
        }

        if (itemId.HasValue)
        {
            var scanResult = await TryMarkItemPickedAsync(id!.Value, itemId.Value);
            if (scanResult != null) return scanResult;
        }

        if (materialIdFromCode.HasValue && !itemId.HasValue)
        {
            var matLine = await _context.OrderMaterialPickLines
                .FirstOrDefaultAsync(l => l.BuildOrderId == id && l.MaterialId == materialIdFromCode.Value);
            if (matLine != null)
            {
                var totals = await GetMergedMaterialComponentTotalsAsync(id.Value, matLine.MaterialId);
                if (totals.Needed > 0)
                {
                    if (totals.Picked >= totals.Needed)
                    {
                        TempData["PickListSuccess"] = "This material is already fully picked.";
                        return RedirectToPage("./PickList", new { id });
                    }
                    matLine.QuantityRequired = totals.Needed;
                }
                // Deduct from material inventory and record movement
                var stocks = await _context.MaterialStocks
                    .Include(ms => ms.Location)
                    .Where(ms => ms.MaterialId == matLine.MaterialId && ms.Quantity > 0)
                    .OrderBy(ms => ms.Location != null ? ms.Location.Zone : "")
                    .ThenBy(ms => ms.Location != null ? ms.Location.Code : "")
                    .ToListAsync();
                if (stocks.Count == 0)
                {
                    TempData["PickListError"] = "No stock available for this material.";
                    return RedirectToPage("./PickList", new { id });
                }
                var stock = stocks[0];
                stock.Quantity -= 1;
                stock.UpdatedAtUtc = DateTime.UtcNow;
                _context.MaterialStockMovements.Add(new MaterialStockMovement
                {
                    MaterialId = matLine.MaterialId,
                    LocationId = stock.LocationId,
                    QuantityDelta = -1,
                    Type = "OUT",
                    Notes = "Pick list",
                    PerformedBy = await GetPerformedByAsync(),
                    TimestampUtc = DateTime.UtcNow,
                    BuildOrderId = id
                });
                matLine.QuantityPicked += 1;
                await _context.SaveChangesAsync();
                var mergedPickedAfter = totals.Needed > 0 ? totals.Picked + 1 : matLine.QuantityPicked;
                var mergedNeededAfter = totals.Needed > 0 ? totals.Needed : matLine.QuantityRequired;
                TempData["PickListSuccess"] = $"Picked 1. ({mergedPickedAfter}/{mergedNeededAfter}) Inventory deducted.";
                return RedirectToPage("./PickList", new { id });
            }
            var mat = await _context.Materials.FindAsync(materialIdFromCode.Value);
            if (mat != null)
            {
                // If this material exists as a material-only component in model/variant, create/track it
                // as a component line instead of an ad-hoc line (avoids duplicate rows in the pick list).
                var totals = await GetMergedMaterialComponentTotalsAsync(id.Value, mat.Id);
                int? modelComponentId = totals.ModelComponentId;
                int? variantComponentId = totals.VariantComponentId;

                if (totals.Needed > 0 && totals.Picked >= totals.Needed)
                {
                    TempData["PickListSuccess"] = "This material is already fully picked.";
                    return RedirectToPage("./PickList", new { id });
                }

                var newLine = new OrderMaterialPickLine
                {
                    BuildOrderId = id.Value,
                    ComponentId = totals.Needed > 0 ? modelComponentId : null,
                    VariantComponentId = totals.Needed > 0 ? variantComponentId : null,
                    MaterialId = mat.Id,
                    QuantityRequired = totals.Needed > 0 ? totals.Needed : 1,
                    QuantityPicked = 1
                };
                _context.OrderMaterialPickLines.Add(newLine);

                // Deduct from inventory for the first picked unit
                var stocks = await _context.MaterialStocks
                    .Include(ms => ms.Location)
                    .Where(ms => ms.MaterialId == mat.Id && ms.Quantity > 0)
                    .OrderBy(ms => ms.Location != null ? ms.Location.Zone : "")
                    .ThenBy(ms => ms.Location != null ? ms.Location.Code : "")
                    .ToListAsync();
                if (stocks.Count == 0)
                {
                    TempData["PickListError"] = "No stock available for this material.";
                    return RedirectToPage("./PickList", new { id });
                }
                var stock = stocks[0];
                stock.Quantity -= 1;
                stock.UpdatedAtUtc = DateTime.UtcNow;
                _context.MaterialStockMovements.Add(new MaterialStockMovement
                {
                    MaterialId = mat.Id,
                    LocationId = stock.LocationId,
                    QuantityDelta = -1,
                    Type = "OUT",
                    Notes = "Pick list",
                    PerformedBy = await GetPerformedByAsync(),
                    TimestampUtc = DateTime.UtcNow,
                    BuildOrderId = id
                });

                await _context.SaveChangesAsync();
                TempData["PickListSuccess"] = $"Added and marked picked: {mat.Name}. Inventory deducted.";
                return RedirectToPage("./PickList", new { id });
            }
        }

        // 3) Material by ID or name (mark picked if already on list; do not add new lines)
        Material? material = null;
        if (int.TryParse(value, out var matId) && matId > 0)
            material = await _context.Materials.FindAsync(matId);
        if (material == null)
            material = await _context.Materials.FirstOrDefaultAsync(m => m.Name.ToLower() == value.ToLower())
                ?? await _context.Materials.FirstOrDefaultAsync(m => m.Name.Contains(value));

        if (material != null)
        {
            var existingLine = await _context.OrderMaterialPickLines
                .FirstOrDefaultAsync(l => l.BuildOrderId == id && l.MaterialId == material.Id);
            if (existingLine != null)
            {
                var totals = await GetMergedMaterialComponentTotalsAsync(id.Value, existingLine.MaterialId);
                if (totals.Needed > 0)
                {
                    if (totals.Picked >= totals.Needed)
                    {
                        TempData["PickListSuccess"] = $"{material.Name} already fully picked ({totals.Picked}/{totals.Needed}).";
                        return RedirectToPage("./PickList", new { id });
                    }
                    existingLine.QuantityRequired = totals.Needed;
                    if (!existingLine.ComponentId.HasValue && !existingLine.VariantComponentId.HasValue)
                    {
                        existingLine.ComponentId = totals.ModelComponentId;
                        existingLine.VariantComponentId = totals.ModelComponentId == null ? totals.VariantComponentId : null;
                    }
                }
                // Deduct from material inventory and record movement
                var stocks = await _context.MaterialStocks
                    .Include(ms => ms.Location)
                    .Where(ms => ms.MaterialId == existingLine.MaterialId && ms.Quantity > 0)
                    .OrderBy(ms => ms.Location != null ? ms.Location.Zone : "")
                    .ThenBy(ms => ms.Location != null ? ms.Location.Code : "")
                    .ToListAsync();
                if (stocks.Count == 0)
                {
                    TempData["PickListError"] = "No stock available for this material.";
                    return RedirectToPage("./PickList", new { id });
                }
                var stock = stocks[0];
                stock.Quantity -= 1;
                stock.UpdatedAtUtc = DateTime.UtcNow;
                _context.MaterialStockMovements.Add(new MaterialStockMovement
                {
                    MaterialId = existingLine.MaterialId,
                    LocationId = stock.LocationId,
                    QuantityDelta = -1,
                    Type = "OUT",
                    Notes = "Pick list",
                    PerformedBy = await GetPerformedByAsync(),
                    TimestampUtc = DateTime.UtcNow,
                    BuildOrderId = id
                });

                existingLine.QuantityPicked += 1;
                await _context.SaveChangesAsync();
                var mergedPickedAfter = totals.Needed > 0 ? totals.Picked + 1 : existingLine.QuantityPicked;
                var mergedNeededAfter = totals.Needed > 0 ? totals.Needed : existingLine.QuantityRequired;
                TempData["PickListSuccess"] = $"Picked {material.Name}. ({mergedPickedAfter}/{mergedNeededAfter}) Inventory deducted.";
                return RedirectToPage("./PickList", new { id });
            }
            TempData["PickListError"] = $"Material \"{material.Name}\" is not on the pick list for this order.";
            return RedirectToPage("./PickList", new { id });
        }

        TempData["PickListError"] = $"\"{value}\" not found. Enter a material set name, material name/ID, or scan a QR code (tag/SKU).";
        return RedirectToPage("./PickList", new { id });
    }

    private async Task<IActionResult?> TryMarkItemPickedAsync(int orderId, int itemId)
    {
        var pickLine = await _context.OrderPickLines
            .FirstOrDefaultAsync(l => l.BuildOrderId == orderId && l.ItemId == itemId);
        if (pickLine == null)
        {
            TempData["PickListError"] = "This item is not on the pick list for this order.";
            return RedirectToPage("./PickList", new { id = orderId });
        }
        var order = await _context.BuildOrders.FindAsync(orderId);
        if (order != null)
        {
            var model = await _context.MachineModels
                .Include(m => m.Components)
                .ThenInclude(c => c.Alternatives)
                .FirstOrDefaultAsync(m => m.Id == order.MachineModelId);
            if (model != null)
            {
                var component = model.Components.FirstOrDefault(c =>
                    c.ItemId == itemId || c.Alternatives.Any(a => a.ItemId == itemId));
                if (component != null && component.ItemId.HasValue)
                {
                    var allItemIds = new List<int> { component.ItemId.Value };
                    allItemIds.AddRange(component.Alternatives.Select(a => a.ItemId));
                    var componentPickLines = await _context.OrderPickLines
                        .Where(l => l.BuildOrderId == orderId && allItemIds.Contains(l.ItemId))
                        .ToListAsync();
                    var totalPicked = componentPickLines.Sum(pl => pl.QuantityPicked);
                    if (totalPicked >= component.Quantity)
                    {
                        TempData["PickListSuccess"] = $"Component requirement already satisfied ({totalPicked}/{component.Quantity}).";
                        return RedirectToPage("./PickList", new { id = orderId });
                    }
                }
            }
        }
        if (pickLine.QuantityPicked >= pickLine.QuantityRequired)
        {
            var it = await _context.Items.FindAsync(itemId);
            TempData["PickListSuccess"] = $"{it?.Sku ?? "Item"} already fully picked ({pickLine.QuantityPicked}/{pickLine.QuantityRequired}).";
            return RedirectToPage("./PickList", new { id = orderId });
        }

        // Deduct from item inventory (StockSnapshots), like the + button does
        var stock = await _context.StockSnapshots
            .Include(s => s.Location)
            .Where(s => s.ItemId == itemId && s.Quantity > 0)
            .OrderBy(s => s.Location != null ? s.Location.Zone : "")
            .ThenBy(s => s.Location != null ? s.Location.Code : "")
            .FirstOrDefaultAsync();
        if (stock == null)
        {
            TempData["PickListError"] = "No stock available for this item at any location.";
            return RedirectToPage("./PickList", new { id = orderId });
        }

        stock.Quantity -= 1;
        stock.UpdatedAtUtc = DateTime.UtcNow;

        pickLine.QuantityPicked += 1;
        await _context.SaveChangesAsync();

        var item = await _context.Items.FindAsync(itemId);
        TempData["PickListSuccess"] = $"Picked {item?.Sku ?? "Item"}. ({pickLine.QuantityPicked}/{pickLine.QuantityRequired}) Inventory deducted.";
        return RedirectToPage("./PickList", new { id = orderId });
    }

    /// <summary>Add a material set to this order's pick list. All materials in the set are added with their quantities.</summary>
    public async Task<IActionResult> OnPostAddMaterialSetAsync(int? id, int? materialSetId)
    {
        if (id == null)
        {
            TempData["PickListError"] = "Order not specified.";
            return RedirectToPage("./Index");
        }
        if (materialSetId == null || materialSetId <= 0)
        {
            TempData["PickListError"] = "Please select a material set.";
            return RedirectToPage("./PickList", new { id });
        }
        var order = await _context.BuildOrders.FindAsync(id);
        if (order == null) return NotFound();
        var set = await _context.MaterialSets
            .Include(s => s.Items)
            .ThenInclude(i => i.Material)
            .FirstOrDefaultAsync(s => s.Id == materialSetId.Value);
        if (set == null || !set.Items.Any())
        {
            TempData["PickListError"] = "Material set not found or has no materials.";
            return RedirectToPage("./PickList", new { id });
        }
        var setItemsGrouped = set.Items
            .Where(i => i.Quantity > 0)
            .GroupBy(i => i.MaterialId)
            .Select(g => (MaterialId: g.Key, Quantity: g.Sum(x => x.Quantity)))
            .ToList();

        if (!setItemsGrouped.Any())
        {
            TempData["PickListError"] = "This material set has no materials (or all quantities are zero). Add materials to the set first.";
            return RedirectToPage("./PickList", new { id });
        }

        var noStock = false;
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            foreach (var item in setItemsGrouped)
            {
                var totals = await GetMergedMaterialComponentTotalsAsync(id.Value, item.MaterialId);
                var existingLine = await FindExistingMaterialPickLineAsync(id.Value, item.MaterialId);
                if (existingLine != null)
                {
                    var remainingNeeded = totals.Needed > 0
                        ? Math.Max(0, totals.Needed - totals.Picked)
                        : Math.Max(0, existingLine.QuantityRequired - existingLine.QuantityPicked);
                    var pickQty = Math.Min(item.Quantity, remainingNeeded);
                    if (pickQty <= 0) continue;

                    var deducted = await DeductMaterialInventoryAsync(id.Value, item.MaterialId, pickQty);
                    if (!deducted) { noStock = true; break; }

                    existingLine.QuantityPicked += pickQty;
                    if (totals.Needed > 0)
                    {
                        existingLine.QuantityRequired = totals.Needed;
                        if (!existingLine.ComponentId.HasValue && !existingLine.VariantComponentId.HasValue)
                        {
                            existingLine.ComponentId = totals.ModelComponentId;
                            existingLine.VariantComponentId = totals.ModelComponentId == null ? totals.VariantComponentId : null;
                        }
                    }
                }
                else
                {
                    var pickQty = totals.Needed > 0
                        ? Math.Min(item.Quantity, Math.Max(0, totals.Needed - totals.Picked))
                        : item.Quantity;

                    if (pickQty <= 0) continue;

                    if (totals.Needed > 0)
                    {
                        _context.OrderMaterialPickLines.Add(new OrderMaterialPickLine
                        {
                            BuildOrderId = id.Value,
                            ComponentId = totals.ModelComponentId,
                            VariantComponentId = totals.ModelComponentId == null ? totals.VariantComponentId : null,
                            MaterialId = item.MaterialId,
                            QuantityRequired = totals.Needed,
                            QuantityPicked = pickQty
                        });
                    }
                    else
                    {
                        _context.OrderMaterialPickLines.Add(new OrderMaterialPickLine
                        {
                            BuildOrderId = id.Value,
                            ComponentId = null,
                            VariantComponentId = null,
                            MaterialId = item.MaterialId,
                            QuantityRequired = item.Quantity,
                            QuantityPicked = pickQty
                        });
                    }

                    var deducted = await DeductMaterialInventoryAsync(id.Value, item.MaterialId, pickQty);
                    if (!deducted) { noStock = true; break; }
                }
            }

            if (noStock)
            {
                await tx.RollbackAsync();
                return;
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        });
        if (noStock)
        {
            TempData["PickListError"] = "No stock available for this material set item.";
            return RedirectToPage("./PickList", new { id });
        }
        TempData["PickListSuccess"] = $"Material set \"{set.Name}\" added and picked ({setItemsGrouped.Count} material(s)).";
        return RedirectToPage("./PickList", new { id });
    }

    /// <summary>Scan QR (tag code or SKU) to mark one as picked.</summary>
    public async Task<IActionResult> OnPostScanAsync(int? id, string? scannedCode)
    {
        if (id == null)
        {
            TempData["PickListError"] = "Order not specified.";
            return RedirectToPage("./Index");
        }
        if (string.IsNullOrWhiteSpace(scannedCode))
        {
            TempData["PickListError"] = "Enter or scan the QR code (tag code or SKU).";
            return RedirectToPage("./PickList", new { id });
        }

        var code = scannedCode.Trim();

        // Resolve to ItemId: by Tag code (what's on the QR label), by SKU, or by Material QR
        var itemId = await _context.Tags
            .Where(t => t.Code == code)
            .Select(t => (int?)t.ItemId)
            .FirstOrDefaultAsync();
        if (itemId == null)
            itemId = await _context.Items.Where(i => i.Sku == code).Select(i => (int?)i.Id).FirstOrDefaultAsync();

        // Try Material QR → find an Item linked to that material, or the pick line for that material
        int? materialId = null;
        if (itemId == null)
        {
            materialId = await _context.Materials
                .Where(m => m.Qr == code)
                .Select(m => (int?)m.Id)
                .FirstOrDefaultAsync();
            if (materialId.HasValue)
            {
                itemId = await _context.Items
                    .Where(i => i.MaterialId == materialId.Value)
                    .Select(i => (int?)i.Id)
                    .FirstOrDefaultAsync();
            }
        }

        // Try Material QR → match directly against OrderPickLine via component's MaterialId
        if (itemId == null && materialId.HasValue)
        {
            var order2 = await _context.BuildOrders.FindAsync(id);
            if (order2 != null)
            {
                var model2 = await _context.MachineModels
                    .Include(m => m.Components)
                    .FirstOrDefaultAsync(m => m.Id == order2.MachineModelId);
                var comp = model2?.Components.FirstOrDefault(c => c.MaterialId == materialId.Value);
                if (comp != null)
                {
                    // Find the pick line for this component (material-only component shown as manual)
                    var pickLineForMaterial = await _context.OrderPickLines
                        .FirstOrDefaultAsync(l => l.BuildOrderId == id.Value);

                    // Material is on the model but has no linked item — can't scan-pick
                    var matName = await _context.Materials.Where(m => m.Id == materialId.Value).Select(m => m.Name).FirstOrDefaultAsync();
                    TempData["PickListError"] = $"Material '{matName}' (QR: {code}) is on this model but has no linked inventory item. Link an item to this material in Catalog → Inventory so scan-to-pick works.";
                    return RedirectToPage("./PickList", new { id });
                }
            }
        }

        if (itemId == null)
        {
            TempData["PickListError"] = $"Code '{code}' not found. Scan the item's QR label or enter a valid tag code / SKU, or a Material QR.";
            return RedirectToPage("./PickList", new { id });
        }

        // Find pick line - check if this item is primary or an alternative
        var pickLine = await _context.OrderPickLines
            .FirstOrDefaultAsync(l => l.BuildOrderId == id && l.ItemId == itemId.Value);
        
        if (pickLine == null)
        {
            TempData["PickListError"] = "This item is not on the pick list for this order.";
            return RedirectToPage("./PickList", new { id });
        }

        // Check if this is part of a component with alternatives
        var order = await _context.BuildOrders.FindAsync(id);
        if (order != null)
        {
            var model = await _context.MachineModels
                .Include(m => m.Components)
                    .ThenInclude(c => c.Alternatives)
                .FirstOrDefaultAsync(m => m.Id == order.MachineModelId);
            
            if (model != null)
            {
                var component = model.Components.FirstOrDefault(c => 
                    c.ItemId == itemId.Value || c.Alternatives.Any(a => a.ItemId == itemId.Value));
                
                if (component != null && component.ItemId.HasValue)
                {
                    var allItemIds = new List<int> { component.ItemId.Value };
                    allItemIds.AddRange(component.Alternatives.Select(a => a.ItemId));
                    var componentPickLines = await _context.OrderPickLines
                        .Where(l => l.BuildOrderId == id && allItemIds.Contains(l.ItemId))
                        .ToListAsync();
                    
                    var totalPicked = componentPickLines.Sum(pl => pl.QuantityPicked);
                    if (totalPicked >= component.Quantity)
                    {
                        var it = await _context.Items.FindAsync(itemId.Value);
                        TempData["PickListSuccess"] = $"Component requirement already satisfied ({totalPicked}/{component.Quantity}). Any alternative can be used.";
                        return RedirectToPage("./PickList", new { id });
                    }
                }
            }
        }

        if (pickLine.QuantityPicked >= pickLine.QuantityRequired)
        {
            var it = await _context.Items.FindAsync(itemId.Value);
            TempData["PickListSuccess"] = $"{it?.Sku ?? "Item"} already fully picked ({pickLine.QuantityPicked}/{pickLine.QuantityRequired}).";
            return RedirectToPage("./PickList", new { id });
        }

        // Deduct from item inventory (StockSnapshots), same rule as manual +
        var stock = await _context.StockSnapshots
            .Include(s => s.Location)
            .Where(s => s.ItemId == itemId.Value && s.Quantity > 0)
            .OrderBy(s => s.Location != null ? s.Location.Zone : "")
            .ThenBy(s => s.Location != null ? s.Location.Code : "")
            .FirstOrDefaultAsync();
        if (stock == null)
        {
            TempData["PickListError"] = "No stock available for this item at any location.";
            return RedirectToPage("./PickList", new { id });
        }

        stock.Quantity -= 1;
        stock.UpdatedAtUtc = DateTime.UtcNow;

        pickLine.QuantityPicked += 1;
        await _context.SaveChangesAsync();

        var item = await _context.Items.FindAsync(itemId.Value);
        var sku = item?.Sku ?? "Item";
        TempData["PickListSuccess"] = $"Picked {sku}. ({pickLine.QuantityPicked}/{pickLine.QuantityRequired}) Inventory deducted.";
        return RedirectToPage("./PickList", new { id });
    }

    /// <summary>
    /// Manual selection: mark an item or material as picked from the dropdown.
    /// selectedValue format:  "i_{itemId}"   for inventory items
    ///                        "mm_{id}"      for model-level material-only components
    ///                        "mv_{id}"      for variant-level material-only components
    ///                        "m_{id}"       legacy (treated as model-level)
    /// </summary>
    public async Task<IActionResult> OnPostMarkPickedAsync(int? id, string? selectedValue)
    {
        if (id == null)
        {
            TempData["PickListError"] = "Order not specified.";
            return RedirectToPage("./Index");
        }
        if (string.IsNullOrWhiteSpace(selectedValue))
        {
            TempData["PickListError"] = "Please select a material or item from the list.";
            return RedirectToPage("./PickList", new { id });
        }

        // ── Ad-hoc material line (from material set) ─────────────────────────
        if (selectedValue.StartsWith("adhoc_") && int.TryParse(selectedValue["adhoc_".Length..], out int adHocLineId))
        {
            var adHocLine = await _context.OrderMaterialPickLines
                .FirstOrDefaultAsync(l => l.Id == adHocLineId && l.BuildOrderId == id);
            if (adHocLine == null)
            {
                TempData["PickListError"] = "Pick list line not found.";
                return RedirectToPage("./PickList", new { id });
            }
            if (adHocLine.QuantityPicked >= adHocLine.QuantityRequired)
            {
                TempData["PickListSuccess"] = "This material set line is already fully picked.";
                return RedirectToPage("./PickList", new { id });
            }
            adHocLine.QuantityPicked += 1;
            await _context.SaveChangesAsync();
            TempData["PickListSuccess"] = $"Picked 1. ({adHocLine.QuantityPicked}/{adHocLine.QuantityRequired})";
            return RedirectToPage("./PickList", new { id });
        }

        // ── Material-only component (model or variant) ───────────────────────
        var isMaterialComponent = selectedValue.StartsWith("mm_") || selectedValue.StartsWith("mv_") || selectedValue.StartsWith("m_");
        if (isMaterialComponent)
        {
            var isVariant = selectedValue.StartsWith("mv_");
            int componentId;
            if (selectedValue.StartsWith("mv_"))
            {
                if (!int.TryParse(selectedValue["mv_".Length..], out componentId))
                {
                    TempData["PickListError"] = "Invalid selection.";
                    return RedirectToPage("./PickList", new { id });
                }
            }
            else if (selectedValue.StartsWith("mm_"))
            {
                if (!int.TryParse(selectedValue["mm_".Length..], out componentId))
                {
                    TempData["PickListError"] = "Invalid selection.";
                    return RedirectToPage("./PickList", new { id });
                }
            }
            else if (selectedValue.StartsWith("m_"))
            {
                if (!int.TryParse(selectedValue[2..], out componentId))
                {
                    TempData["PickListError"] = "Invalid selection.";
                    return RedirectToPage("./PickList", new { id });
                }
            }
            else
            {
                TempData["PickListError"] = "Invalid selection.";
                return RedirectToPage("./PickList", new { id });
            }

            var order = await _context.BuildOrders.FindAsync(id);
            if (order == null) return NotFound();

            MachineModelComponent? mmComp = null;
            MachineVariantComponent? mvComp = null;
            if (isVariant)
            {
                var variant = await _context.MachineVariants
                    .Include(v => v.Components)
                        .ThenInclude(c => c.Material)
                    .FirstOrDefaultAsync(v => v.Id == order.MachineVariantId);
                mvComp = variant?.Components.FirstOrDefault(c => c.Id == componentId);
            }
            else
            {
                var model = await _context.MachineModels
                    .Include(m => m.Components)
                        .ThenInclude(c => c.Material)
                    .FirstOrDefaultAsync(m => m.Id == order.MachineModelId);
                mmComp = model?.Components.FirstOrDefault(c => c.Id == componentId);
            }

            var materialId = isVariant ? mvComp?.MaterialId : mmComp?.MaterialId;
            if (materialId == null || materialId.Value <= 0)
            {
                TempData["PickListError"] = "Component not found on this order's model.";
                return RedirectToPage("./PickList", new { id });
            }

            var matPickLine = await _context.OrderMaterialPickLines
                .FirstOrDefaultAsync(l => l.BuildOrderId == id && (isVariant ? l.VariantComponentId == componentId : l.ComponentId == componentId));

            if (matPickLine == null)
            {
                var totalsForMaterial = await GetMergedMaterialComponentTotalsAsync(id.Value, materialId.Value);
                var mergedNeeded = totalsForMaterial.Needed > 0 ? totalsForMaterial.Needed : (isVariant ? (mvComp?.Quantity ?? 1) : (mmComp?.Quantity ?? 1));
                matPickLine = new OrderMaterialPickLine
                {
                    BuildOrderId = id.Value,
                    ComponentId = isVariant ? null : componentId,
                    VariantComponentId = isVariant ? componentId : null,
                    MaterialId = materialId.Value,
                    QuantityRequired = mergedNeeded,
                    QuantityPicked = 0
                };
                _context.OrderMaterialPickLines.Add(matPickLine);
            }

            // Full check must use merged totals across model + variant definitions for this same material.
            var totalsForMaterial2 = await GetMergedMaterialComponentTotalsAsync(id.Value, materialId.Value);
            if (totalsForMaterial2.Needed > 0 && totalsForMaterial2.Picked >= totalsForMaterial2.Needed)
            {
                var matNameFull = isVariant ? (mvComp?.Material?.Name ?? "Material") : (mmComp?.Material?.Name ?? "Material");
                TempData["PickListSuccess"] = $"{matNameFull} already fully picked ({totalsForMaterial2.Picked}/{totalsForMaterial2.Needed}).";
                return RedirectToPage("./PickList", new { id });
            }

            if (totalsForMaterial2.Needed > 0)
                matPickLine.QuantityRequired = totalsForMaterial2.Needed;

            // Deduct from material inventory and record movement
            var stocks = await _context.MaterialStocks
                .Include(ms => ms.Location)
                .Where(ms => ms.MaterialId == matPickLine.MaterialId && ms.Quantity > 0)
                .OrderBy(ms => ms.Location != null ? ms.Location.Zone : "")
                .ThenBy(ms => ms.Location != null ? ms.Location.Code : "")
                .ToListAsync();
            if (stocks.Count == 0)
            {
                TempData["PickListError"] = "No stock available for this material.";
                return RedirectToPage("./PickList", new { id });
            }
            var stock = stocks[0];
            stock.Quantity -= 1;
            stock.UpdatedAtUtc = DateTime.UtcNow;
            _context.MaterialStockMovements.Add(new MaterialStockMovement
            {
                MaterialId = matPickLine.MaterialId,
                LocationId = stock.LocationId,
                QuantityDelta = -1,
                Type = "OUT",
                Notes = "Pick list",
                PerformedBy = await GetPerformedByAsync(),
                TimestampUtc = DateTime.UtcNow,
                BuildOrderId = id
            });

            matPickLine.QuantityPicked += 1;
            await _context.SaveChangesAsync();

            var matName = isVariant ? (mvComp?.Material?.Name ?? "Material") : (mmComp?.Material?.Name ?? "Material");
            var mergedPickedAfter = totalsForMaterial2.Picked + 1;
            var mergedNeededForMsg = totalsForMaterial2.Needed > 0 ? totalsForMaterial2.Needed : matPickLine.QuantityRequired;
            TempData["PickListSuccess"] = $"Picked {matName}. ({mergedPickedAfter}/{mergedNeededForMsg}) Inventory deducted.";
            return RedirectToPage("./PickList", new { id });
        }

        // ── Inventory item ───────────────────────────────────────────────────
        if (!selectedValue.StartsWith("i_") || !int.TryParse(selectedValue[2..], out int itemId))
        {
            TempData["PickListError"] = "Invalid selection.";
            return RedirectToPage("./PickList", new { id });
        }

        // Find pick line for this order and item
        var pickLine = await _context.OrderPickLines
            .FirstOrDefaultAsync(l => l.BuildOrderId == id && l.ItemId == itemId);

        if (pickLine == null)
        {
            TempData["PickListError"] = "Selected item is not on the pick list for this order.";
            return RedirectToPage("./PickList", new { id });
        }

        // If part of a component with alternatives, check total picked for the component
        var orderForItem = await _context.BuildOrders.FindAsync(id);
        if (orderForItem != null)
        {
            var modelForItem = await _context.MachineModels
                .Include(m => m.Components)
                    .ThenInclude(c => c.Alternatives)
                .FirstOrDefaultAsync(m => m.Id == orderForItem.MachineModelId);

            if (modelForItem != null)
            {
                var component = modelForItem.Components.FirstOrDefault(c =>
                    c.ItemId == itemId || c.Alternatives.Any(a => a.ItemId == itemId));

                if (component != null && component.ItemId.HasValue)
                {
                    var allItemIds = new List<int> { component.ItemId.Value };
                    allItemIds.AddRange(component.Alternatives.Select(a => a.ItemId));
                    var componentPickLines = await _context.OrderPickLines
                        .Where(l => l.BuildOrderId == id && allItemIds.Contains(l.ItemId))
                        .ToListAsync();

                    var totalPicked = componentPickLines.Sum(pl => pl.QuantityPicked);
                    if (totalPicked >= component.Quantity)
                    {
                        TempData["PickListSuccess"] = $"Component requirement already satisfied ({totalPicked}/{component.Quantity}).";
                        return RedirectToPage("./PickList", new { id });
                    }
                }
            }
        }

        if (pickLine.QuantityPicked >= pickLine.QuantityRequired)
        {
            var it = await _context.Items.FindAsync(itemId);
            TempData["PickListSuccess"] = $"{it?.Sku ?? "Item"} already fully picked ({pickLine.QuantityPicked}/{pickLine.QuantityRequired}).";
            return RedirectToPage("./PickList", new { id });
        }

        pickLine.QuantityPicked += 1;
        await _context.SaveChangesAsync();

        var item = await _context.Items.FindAsync(itemId);
        var sku = item?.Sku ?? "Item";
        TempData["PickListSuccess"] = $"Picked {sku}. ({pickLine.QuantityPicked}/{pickLine.QuantityRequired})";
        return RedirectToPage("./PickList", new { id });
    }

    /// <summary>
    /// Delete an ad-hoc material pick line (for example, one added manually or from a material set) from this order's pick list.
    /// Only lines with ComponentId == null are affected so we don't modify model-based components.
    /// </summary>
    public async Task<IActionResult> OnPostDeleteAdHocLineAsync(int? id, int? orderMaterialPickLineId)
    {
        if (id == null)
        {
            TempData["PickListError"] = "Order not specified.";
            return RedirectToPage("./Index");
        }
        if (orderMaterialPickLineId == null || orderMaterialPickLineId <= 0)
        {
            TempData["PickListError"] = "Line not specified.";
            return RedirectToPage("./PickList", new { id });
        }

        var line = await _context.OrderMaterialPickLines
            .FirstOrDefaultAsync(l => l.Id == orderMaterialPickLineId.Value && l.BuildOrderId == id && l.ComponentId == null && l.VariantComponentId == null);
        if (line == null)
        {
            TempData["PickListError"] = "Material line not found or cannot be deleted.";
            return RedirectToPage("./PickList", new { id });
        }

        // If the line has already deducted inventory (picked > 0), return it to the warehouse before deleting.
        var performedBy = await GetPerformedByAsync();
        var qtyToReturn = Math.Max(0, line.QuantityPicked);

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            if (qtyToReturn > 0)
            {
                // Reverse the most recent OUT movements for this order/material back into stock, preserving locations.
                // This handles the "material set" deduction which can take from multiple locations.
                var outs = await _context.MaterialStockMovements
                    .Where(m =>
                        m.MaterialId == line.MaterialId &&
                        m.BuildOrderId == id &&
                        m.QuantityDelta < 0)
                    .OrderByDescending(m => m.TimestampUtc)
                    .ToListAsync();

                var remaining = qtyToReturn;
                foreach (var mv in outs)
                {
                    if (remaining <= 0) break;
                    var taken = Math.Min(remaining, Math.Abs(mv.QuantityDelta));
                    if (taken <= 0) continue;

                    var stockRow = await _context.MaterialStocks
                        .FirstOrDefaultAsync(ms => ms.MaterialId == line.MaterialId && ms.LocationId == mv.LocationId);
                    if (stockRow == null)
                    {
                        stockRow = new MaterialStock
                        {
                            MaterialId = line.MaterialId,
                            LocationId = mv.LocationId,
                            Quantity = 0,
                            UpdatedAtUtc = DateTime.UtcNow
                        };
                        _context.MaterialStocks.Add(stockRow);
                    }

                    stockRow.Quantity += taken;
                    stockRow.UpdatedAtUtc = DateTime.UtcNow;

                    _context.MaterialStockMovements.Add(new MaterialStockMovement
                    {
                        MaterialId = line.MaterialId,
                        LocationId = mv.LocationId,
                        QuantityDelta = taken,
                        Type = "IN",
                        Notes = "Return from pick list (deleted ad-hoc line)",
                        PerformedBy = performedBy,
                        TimestampUtc = DateTime.UtcNow,
                        BuildOrderId = null
                    });

                    remaining -= taken;
                }

                // If we cannot find matching OUT movements (edge case), fall back to first known stock location.
                if (remaining > 0)
                {
                    var anyStock = await _context.MaterialStocks
                        .Where(ms => ms.MaterialId == line.MaterialId)
                        .OrderBy(ms => ms.LocationId)
                        .FirstOrDefaultAsync();
                    if (anyStock != null)
                    {
                        anyStock.Quantity += remaining;
                        anyStock.UpdatedAtUtc = DateTime.UtcNow;
                        _context.MaterialStockMovements.Add(new MaterialStockMovement
                        {
                            MaterialId = line.MaterialId,
                            LocationId = anyStock.LocationId,
                            QuantityDelta = remaining,
                            Type = "IN",
                            Notes = "Return from pick list (deleted ad-hoc line; fallback location)",
                            PerformedBy = performedBy,
                            TimestampUtc = DateTime.UtcNow,
                            BuildOrderId = null
                        });
                        remaining = 0;
                    }
                }
            }

            _context.OrderMaterialPickLines.Remove(line);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        });

        TempData["PickListSuccess"] = qtyToReturn > 0
            ? $"Material line removed and {qtyToReturn} returned to inventory."
            : "Material line removed from this order's pick list.";
        return RedirectToPage("./PickList", new { id });
    }

    /// <summary>
    /// Update quantity of an ad-hoc material line on the pick list.
    /// </summary>
    public async Task<IActionResult> OnPostUpdateAdHocLineAsync(int? id, int? orderMaterialPickLineId, int quantityRequired)
    {
        if (id == null)
        {
            TempData["PickListError"] = "Order not specified.";
            return RedirectToPage("./Index");
        }
        if (orderMaterialPickLineId == null || orderMaterialPickLineId <= 0)
        {
            TempData["PickListError"] = "Line not specified.";
            return RedirectToPage("./PickList", new { id });
        }

        var line = await _context.OrderMaterialPickLines
            .FirstOrDefaultAsync(l => l.Id == orderMaterialPickLineId.Value && l.BuildOrderId == id && l.ComponentId == null && l.VariantComponentId == null);
        if (line == null)
        {
            TempData["PickListError"] = "Material line not found or cannot be edited.";
            return RedirectToPage("./PickList", new { id });
        }

        if (quantityRequired < 0)
            quantityRequired = 0;

        line.QuantityRequired = quantityRequired;
        if (line.QuantityPicked > quantityRequired)
            line.QuantityPicked = quantityRequired;

        await _context.SaveChangesAsync();

        TempData["PickListSuccess"] = "Material line updated.";
        return RedirectToPage("./PickList", new { id });
    }

    /// <summary>+ button: add one to picked and deduct from inventory.</summary>
    public async Task<IActionResult> OnPostIncrementPickedAsync(int? id, int? orderMaterialPickLineId, int? componentId)
    {
        if (id == null)
        {
            TempData["PickListError"] = "Order not specified.";
            return RedirectToPage("./Index");
        }
        var order = await _context.BuildOrders.FindAsync(id);
        if (order == null) return NotFound();

        OrderMaterialPickLine? matLine = null;
        if (orderMaterialPickLineId.HasValue && orderMaterialPickLineId.Value > 0)
        {
            matLine = await _context.OrderMaterialPickLines
                .FirstOrDefaultAsync(l => l.Id == orderMaterialPickLineId.Value && l.BuildOrderId == id);
        }
        else if (componentId.HasValue && componentId.Value > 0)
        {
            var model = await _context.MachineModels
                .Include(m => m.Components).ThenInclude(c => c.Material)
                .FirstOrDefaultAsync(m => m.Id == order.MachineModelId);
            var comp = model?.Components.FirstOrDefault(c => c.Id == componentId.Value);
            if (comp == null || !comp.MaterialId.HasValue)
            {
                TempData["PickListError"] = "Component not found.";
                return RedirectToPage("./PickList", new { id });
            }
            matLine = await _context.OrderMaterialPickLines
                .FirstOrDefaultAsync(l => l.BuildOrderId == id && l.ComponentId == componentId.Value);
            if (matLine == null)
            {
                matLine = new OrderMaterialPickLine
                {
                    BuildOrderId = id.Value,
                    ComponentId = componentId.Value,
                    MaterialId = comp.MaterialId.Value,
                    QuantityRequired = comp.Quantity,
                    QuantityPicked = 0
                };
                _context.OrderMaterialPickLines.Add(matLine);
            }
        }

        if (matLine == null)
        {
            TempData["PickListError"] = "Please specify a material line.";
            return RedirectToPage("./PickList", new { id });
        }
        int mergedNeeded = matLine.QuantityRequired;
        int mergedPickedBefore = matLine.QuantityPicked;
        if (matLine.ComponentId.HasValue || matLine.VariantComponentId.HasValue)
        {
            var totals = await GetMergedMaterialComponentTotalsAsync(id.Value, matLine.MaterialId);
            if (totals.Needed > 0)
            {
                mergedNeeded = totals.Needed;
                mergedPickedBefore = totals.Picked;
                // Sync denominator for correct full checks/messages.
                matLine.QuantityRequired = mergedNeeded;
            }
        }

        if (mergedPickedBefore >= mergedNeeded)
        {
            TempData["PickListSuccess"] = "Already at required quantity.";
            return RedirectToPage("./PickList", new { id });
        }

        var stocks = await _context.MaterialStocks
            .Include(ms => ms.Location)
            .Where(ms => ms.MaterialId == matLine.MaterialId && ms.Quantity > 0)
            .OrderBy(ms => ms.Location != null ? ms.Location.Zone : "")
            .ThenBy(ms => ms.Location != null ? ms.Location.Code : "")
            .ToListAsync();
        if (stocks.Count == 0)
        {
            TempData["PickListError"] = "No stock available for this material.";
            return RedirectToPage("./PickList", new { id });
        }
        var stock = stocks[0];
        stock.Quantity -= 1;
        stock.UpdatedAtUtc = DateTime.UtcNow;
        _context.MaterialStockMovements.Add(new MaterialStockMovement
        {
            MaterialId = matLine.MaterialId,
            LocationId = stock.LocationId,
            QuantityDelta = -1,
            Type = "OUT",
            Notes = "Pick list",
            PerformedBy = await GetPerformedByAsync(),
            TimestampUtc = DateTime.UtcNow,
            BuildOrderId = id
        });
        matLine.QuantityPicked += 1;
        await _context.SaveChangesAsync();
        var mergedPickedAfter = mergedPickedBefore + 1;
        TempData["PickListSuccess"] = $"Picked +1 ({mergedPickedAfter}/{mergedNeeded}). Inventory deducted.";
        return RedirectToPage("./PickList", new { id });
    }

    /// <summary>- button: subtract one from picked and add back to inventory.</summary>
    public async Task<IActionResult> OnPostDecrementPickedAsync(int? id, int? orderMaterialPickLineId)
    {
        if (id == null)
        {
            TempData["PickListError"] = "Order not specified.";
            return RedirectToPage("./Index");
        }
        if (!orderMaterialPickLineId.HasValue || orderMaterialPickLineId.Value <= 0)
        {
            TempData["PickListError"] = "Line not specified.";
            return RedirectToPage("./PickList", new { id });
        }
        var matLine = await _context.OrderMaterialPickLines
            .FirstOrDefaultAsync(l => l.Id == orderMaterialPickLineId.Value && l.BuildOrderId == id);
        if (matLine == null)
        {
            TempData["PickListError"] = "Material line not found.";
            return RedirectToPage("./PickList", new { id });
        }
        if (matLine.QuantityPicked <= 0)
        {
            TempData["PickListSuccess"] = "Picked quantity is already 0.";
            return RedirectToPage("./PickList", new { id });
        }

        int locationId;
        var lastOut = await _context.MaterialStockMovements
            .Where(m => m.BuildOrderId == id && m.MaterialId == matLine.MaterialId && m.QuantityDelta < 0)
            .OrderByDescending(m => m.TimestampUtc)
            .FirstOrDefaultAsync();
        if (lastOut != null)
            locationId = lastOut.LocationId;
        else
        {
            var anyStock = await _context.MaterialStocks
                .FirstOrDefaultAsync(ms => ms.MaterialId == matLine.MaterialId);
            if (anyStock == null)
            {
                TempData["PickListError"] = "No stock location for this material; cannot return.";
                return RedirectToPage("./PickList", new { id });
            }
            locationId = anyStock.LocationId;
        }

        var stockRow = await _context.MaterialStocks
            .FirstOrDefaultAsync(ms => ms.MaterialId == matLine.MaterialId && ms.LocationId == locationId);
        if (stockRow == null)
        {
            stockRow = new MaterialStock
            {
                MaterialId = matLine.MaterialId,
                LocationId = locationId,
                Quantity = 0,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _context.MaterialStocks.Add(stockRow);
        }
        stockRow.Quantity += 1;
        stockRow.UpdatedAtUtc = DateTime.UtcNow;
        _context.MaterialStockMovements.Add(new MaterialStockMovement
        {
            MaterialId = matLine.MaterialId,
            LocationId = locationId,
            QuantityDelta = 1,
            Type = "IN",
            Notes = "Return from pick list",
            PerformedBy = await GetPerformedByAsync(),
            TimestampUtc = DateTime.UtcNow,
            BuildOrderId = null
        });
        matLine.QuantityPicked -= 1;
        await _context.SaveChangesAsync();
        TempData["PickListSuccess"] = $"Returned 1 to inventory ({matLine.QuantityPicked}/{matLine.QuantityRequired}).";
        return RedirectToPage("./PickList", new { id, filter = Request.Query["filter"].FirstOrDefault() });
    }

    /// <summary>+ button for item/SKU lines: add one to picked and deduct from item inventory (StockSnapshot).</summary>
    public async Task<IActionResult> OnPostIncrementItemPickedAsync(int? id, int? itemId)
    {
        if (id == null || !itemId.HasValue || itemId.Value <= 0)
        {
            TempData["PickListError"] = "Order and item are required.";
            return RedirectToPage("./PickList", new { id, filter = Request.Query["filter"].FirstOrDefault() });
        }
        var pickLine = await _context.OrderPickLines
            .FirstOrDefaultAsync(l => l.BuildOrderId == id && l.ItemId == itemId.Value);
        if (pickLine == null)
        {
            TempData["PickListError"] = "This item is not on the pick list for this order.";
            return RedirectToPage("./PickList", new { id, filter = Request.Query["filter"].FirstOrDefault() });
        }
        if (pickLine.QuantityPicked >= pickLine.QuantityRequired)
        {
            TempData["PickListSuccess"] = "Already at required quantity.";
            return RedirectToPage("./PickList", new { id, filter = Request.Query["filter"].FirstOrDefault() });
        }
        var stock = await _context.StockSnapshots
            .Include(s => s.Location)
            .Where(s => s.ItemId == itemId.Value && s.Quantity > 0)
            .OrderBy(s => s.Location != null ? s.Location.Zone : "")
            .ThenBy(s => s.Location != null ? s.Location.Code : "")
            .FirstOrDefaultAsync();
        if (stock == null)
        {
            TempData["PickListError"] = "No stock available for this item at any location.";
            return RedirectToPage("./PickList", new { id, filter = Request.Query["filter"].FirstOrDefault() });
        }
        stock.Quantity -= 1;
        stock.UpdatedAtUtc = DateTime.UtcNow;
        pickLine.QuantityPicked += 1;
        await _context.SaveChangesAsync();
        TempData["PickListSuccess"] = $"Picked +1 ({pickLine.QuantityPicked}/{pickLine.QuantityRequired}). Inventory deducted.";
        return RedirectToPage("./PickList", new { id, filter = Request.Query["filter"].FirstOrDefault() });
    }

    /// <summary>- button for item/SKU lines: subtract one from picked and add back to item inventory (StockSnapshot).</summary>
    public async Task<IActionResult> OnPostDecrementItemPickedAsync(int? id, int? itemId)
    {
        if (id == null || !itemId.HasValue || itemId.Value <= 0)
        {
            TempData["PickListError"] = "Order and item are required.";
            return RedirectToPage("./PickList", new { id, filter = Request.Query["filter"].FirstOrDefault() });
        }
        var pickLine = await _context.OrderPickLines
            .FirstOrDefaultAsync(l => l.BuildOrderId == id && l.ItemId == itemId.Value);
        if (pickLine == null || pickLine.QuantityPicked <= 0)
        {
            TempData["PickListSuccess"] = pickLine == null ? "Item not on pick list." : "Picked quantity is already 0.";
            return RedirectToPage("./PickList", new { id, filter = Request.Query["filter"].FirstOrDefault() });
        }
        var stock = await _context.StockSnapshots
            .Include(s => s.Location)
            .Where(s => s.ItemId == itemId.Value)
            .OrderBy(s => s.Location != null ? s.Location.Zone : "")
            .ThenBy(s => s.Location != null ? s.Location.Code : "")
            .FirstOrDefaultAsync();
        if (stock == null)
        {
            var loc = await _context.Locations.OrderBy(l => l.Zone).ThenBy(l => l.Code).FirstOrDefaultAsync();
            if (loc == null)
            {
                TempData["PickListError"] = "No location defined; cannot return item to inventory.";
                return RedirectToPage("./PickList", new { id, filter = Request.Query["filter"].FirstOrDefault() });
            }
            stock = new StockSnapshot { ItemId = itemId.Value, LocationId = loc.Id, Quantity = 0, UpdatedAtUtc = DateTime.UtcNow };
            _context.StockSnapshots.Add(stock);
        }
        stock.Quantity += 1;
        stock.UpdatedAtUtc = DateTime.UtcNow;
        pickLine.QuantityPicked -= 1;
        await _context.SaveChangesAsync();
        TempData["PickListSuccess"] = $"Returned 1 to inventory ({pickLine.QuantityPicked}/{pickLine.QuantityRequired}).";
        return RedirectToPage("./PickList", new { id, filter = Request.Query["filter"].FirstOrDefault() });
    }
}

public class PickListLine
{
    public int? ComponentId { get; set; }
    public int? VariantComponentId { get; set; }
    /// <summary>When set, this line is from a material set (ad-hoc); use for manual pick value.</summary>
    public int? OrderMaterialPickLineId { get; set; }
    public int ItemId { get; set; }
    public int? MaterialId { get; set; }
    public string? MaterialName { get; set; }
    /// <summary>Material reference (supplier part number) for display in search.</summary>
    public string? MaterialReference { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string ModelOrType { get; set; } = "";
    public int QuantityNeeded { get; set; }
    public int QuantityPicked { get; set; }
    public string? Notes { get; set; }
    public int TotalAvailable { get; set; }
    public List<PickListLocation> Locations { get; set; } = new();
    public List<PickListAlternative> Alternatives { get; set; } = new();
}

public class PickListAlternative
{
    public int ItemId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string ModelOrType { get; set; } = "";
    public int QuantityPicked { get; set; }
    public int TotalAvailable { get; set; }
    public List<PickListLocation> Locations { get; set; } = new();
}

public class PickListLocation
{
    public string Code { get; set; } = "";
    public string Zone { get; set; } = "";
    public int Quantity { get; set; }
}

public class MaterialSetSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int ItemCount { get; set; }
}
