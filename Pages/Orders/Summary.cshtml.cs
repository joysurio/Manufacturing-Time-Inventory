using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Execution;
using System.Text;

namespace ManufacturingTimeTracking.Pages.Orders;

public class SummaryModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public SummaryModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public BuildOrder BuildOrder { get; set; } = default!;
    public BuildExecution? LatestExecution { get; set; }
    public string MachineModelName { get; set; } = "";
    public string MachineVariantName { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null) return NotFound();

        var order = await _context.BuildOrders
            .Include(o => o.Executions)
                .ThenInclude(e => e.Phases)
                    .ThenInclude(p => p.Steps)
                        .ThenInclude(s => s.Runs)
                            .ThenInclude(r => r.Workstation)
            .Include(o => o.Executions)
                .ThenInclude(e => e.Phases)
                    .ThenInclude(p => p.Steps)
                        .ThenInclude(s => s.Evidence)
                            .ThenInclude(e => e.Media)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null)
        {
            return NotFound();
        }

        BuildOrder = order;
        LatestExecution = order.Executions.OrderByDescending(e => e.StartedAt).FirstOrDefault();

        var model = await _context.MachineModels.FindAsync(order.MachineModelId);
        var variant = await _context.MachineVariants.FindAsync(order.MachineVariantId);
        MachineModelName = model?.Name ?? $"Model {order.MachineModelId}";
        MachineVariantName = variant?.Name ?? $"Variant {order.MachineVariantId}";

        return Page();
    }

    public async Task<IActionResult> OnGetExportCsvAsync(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var order = await _context.BuildOrders
            .Include(o => o.Executions)
                .ThenInclude(e => e.Phases)
                    .ThenInclude(p => p.Steps)
                        .ThenInclude(s => s.Runs)
                            .ThenInclude(r => r.Workstation)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null)
        {
            return NotFound();
        }

        var execution = order.Executions.OrderByDescending(e => e.StartedAt).FirstOrDefault();
        if (execution == null)
        {
            return NotFound();
        }

        var csv = new StringBuilder();
        csv.AppendLine("Order Summary");
        csv.AppendLine($"External Ref,{order.ExternalRef}");
        csv.AppendLine($"Serial Number,{order.SerialNumber}");
        csv.AppendLine($"Status,{order.Status}");
        csv.AppendLine();
        csv.AppendLine("Phase,Step,Status,Skip Reason,Total Time (seconds),Total Time (HH:MM:SS),Runs,Workstations");
        
        foreach (var phase in execution.Phases.OrderBy(p => p.SortOrder))
        {
            foreach (var step in phase.Steps.OrderBy(s => s.SortOrder))
            {
                var totalSeconds = step.Runs.Where(r => r.DurationSeconds.HasValue).Sum(r => r.DurationSeconds.Value);
                var timeSpan = TimeSpan.FromSeconds(totalSeconds);
                var runs = step.Runs.Where(r => r.DurationSeconds.HasValue).Count();
                var workstations = string.Join("; ", step.Runs
                    .Where(r => r.Workstation != null)
                    .Select(r => r.Workstation!.Name)
                    .Distinct());

                csv.AppendLine($"{phase.Name},{step.Title},{step.State},\"{step.SkipReason ?? ""}\",{totalSeconds},{timeSpan.ToString(@"hh\:mm\:ss")},{runs},\"{workstations}\"");
            }
        }

        var fileName = $"Order_{order.ExternalRef}_{order.SerialNumber}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", fileName);
    }
}
