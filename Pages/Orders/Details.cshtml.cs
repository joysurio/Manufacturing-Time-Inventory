using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Execution;

namespace ManufacturingTimeTracking.Pages.Orders;

public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public DetailsModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public BuildOrder BuildOrder { get; set; } = default!;
    public string MachineModelName { get; set; } = "";
    public string MachineVariantName { get; set; } = "";
    public List<ProductionInterruption> ActiveInterruptions { get; set; } = new();
    /// <summary>Total real hours from StepRun.DurationSeconds for this order (issue #13). Computed, not stored.</summary>
    public decimal RealHours { get; set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null) return NotFound();

        var order = await _context.BuildOrders
            .Include(o => o.BlockedReason)
            .Include(o => o.Executions)
                .ThenInclude(e => e.Phases)
                    .ThenInclude(p => p.Steps)
                        .ThenInclude(s => s.Runs)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null) return NotFound();

        BuildOrder = order;
        var model = await _context.MachineModels.FindAsync(order.MachineModelId);
        var variant = await _context.MachineVariants.FindAsync(order.MachineVariantId);
        MachineModelName = model?.Name ?? $"Model {order.MachineModelId}";
        MachineVariantName = variant?.Name ?? $"Variant {order.MachineVariantId}";
        ActiveInterruptions = await _context.ProductionInterruptions
            .Where(pi => pi.BuildOrderId == order.Id && pi.EndTime == null)
            .OrderByDescending(pi => pi.StartTime)
            .ToListAsync();
        RealHours = order.Executions
            .SelectMany(e => e.Phases)
            .SelectMany(p => p.Steps)
            .SelectMany(s => s.Runs)
            .Where(r => r.DurationSeconds.HasValue)
            .Sum(r => r.DurationSeconds!.Value) / 3600m;
        return Page();
    }
}
