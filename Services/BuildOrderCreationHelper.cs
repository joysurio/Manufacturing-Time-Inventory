using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Catalog;
using ManufacturingTimeTracking.Models.Execution;
using Microsoft.EntityFrameworkCore;

namespace ManufacturingTimeTracking.Services;

public static class BuildOrderCreationHelper
{
    public static async Task PopulatePickLinesAsync(ApplicationDbContext context, BuildOrder order, CancellationToken cancellationToken = default)
    {
        var machineModel = await context.MachineModels
            .Include(m => m.Components)
                .ThenInclude(c => c.Alternatives)
            .FirstOrDefaultAsync(m => m.Id == order.MachineModelId, cancellationToken);
        var machineVariant = await context.MachineVariants
            .Include(v => v.Components)
                .ThenInclude(c => c.Alternatives)
            .FirstOrDefaultAsync(v => v.Id == order.MachineVariantId, cancellationToken);

        async Task AddComponentAsync(int? itemId, int? materialId, int qty, IEnumerable<int> altItemIds)
        {
            var itemIdsToAdd = new List<int>();
            if (itemId.HasValue)
            {
                itemIdsToAdd.Add(itemId.Value);
                itemIdsToAdd.AddRange(altItemIds);
            }
            else if (materialId.HasValue)
            {
                var itemIdForMaterial = await context.Items
                    .Where(i => i.MaterialId == materialId.Value)
                    .Select(i => (int?)i.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (itemIdForMaterial.HasValue)
                    itemIdsToAdd.Add(itemIdForMaterial.Value);
            }

            foreach (var itemIdToAdd in itemIdsToAdd.Distinct())
            {
                context.OrderPickLines.Add(new OrderPickLine
                {
                    BuildOrderId = order.Id,
                    ItemId = itemIdToAdd,
                    QuantityRequired = qty,
                    QuantityPicked = 0
                });
            }
        }

        foreach (var component in machineModel?.Components ?? new List<MachineModelComponent>())
            await AddComponentAsync(component.ItemId, component.MaterialId, component.Quantity, component.Alternatives.Select(a => a.ItemId));
        foreach (var component in machineVariant?.Components ?? new List<MachineVariantComponent>())
            await AddComponentAsync(component.ItemId, component.MaterialId, component.Quantity, component.Alternatives.Select(a => a.ItemId));
    }

    public static string CurrentPlannedWeek()
    {
        var now = DateTime.UtcNow;
        var week = System.Globalization.ISOWeek.GetWeekOfYear(now);
        var year = System.Globalization.ISOWeek.GetYear(now);
        return $"{year}-W{week:D2}";
    }
}
