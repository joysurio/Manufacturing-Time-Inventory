using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Catalog;

namespace ManufacturingTimeTracking.Services;

/// <summary>
/// Syncs materials from template steps (Screws/Materials per step) to the machine model's packing list (components)
/// so they appear automatically when creating orders and pick lists.
/// </summary>
public static class PackingListSync
{
    /// <summary>
    /// For the given machine model, recompute packing list components from all step materials
    /// in all process templates for that machine. Adds or updates MaterialId-based components;
    /// does not remove manually added components.
    /// </summary>
    public static async Task SyncFromTemplateStepsAsync(ApplicationDbContext context, int machineModelId, CancellationToken cancellationToken = default)
    {
        var stepIdsForMachine = await context.ProcessTemplates
            .Where(pt => pt.MachineModelId == machineModelId)
            .SelectMany(pt => pt.Phases)
            .SelectMany(p => p.Steps)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var stepMaterials = await context.StepTemplateMaterials
            .Where(m => stepIdsForMachine.Contains(m.StepTemplateId))
            .ToListAsync(cancellationToken);

        var byMaterial = stepMaterials
            .GroupBy(m => m.MaterialId)
            .Select(g => new { MaterialId = g.Key, TotalQty = g.Sum(x => x.Qty ?? 0) })
            .Where(x => x.MaterialId > 0)
            .ToList();

        var existingComponents = await context.MachineModelComponents
            .Where(c => c.MachineModelId == machineModelId && c.MaterialId != null)
            .ToListAsync(cancellationToken);

        foreach (var item in byMaterial)
        {
            var quantity = (int)Math.Max(1, Math.Round(item.TotalQty));
            var existing = existingComponents.FirstOrDefault(c => c.MaterialId == item.MaterialId);
            if (existing != null)
            {
                existing.Quantity = quantity;
                existing.ItemId = null; // keep component as material-based
            }
            else
            {
                context.MachineModelComponents.Add(new MachineModelComponent
                {
                    MachineModelId = machineModelId,
                    MaterialId = item.MaterialId,
                    ItemId = null,
                    Quantity = quantity,
                    Notes = "From template steps"
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
