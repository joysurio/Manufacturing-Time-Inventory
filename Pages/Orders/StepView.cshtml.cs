using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Execution;
using ManufacturingTimeTracking.Models.Templates;

namespace ManufacturingTimeTracking.Pages.Orders;

[Authorize]
public class StepViewModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public StepViewModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public StepExec StepExec { get; set; } = default!;
    public BuildOrder BuildOrder { get; set; } = default!;
    public StepRun? CurrentRun { get; set; }
    /// <summary>UTC start time as Unix milliseconds (for timer; timezone-independent elapsed from 00:00:00).</summary>
    public long? CurrentRunStartedAtEpochMs { get; set; }
    public List<StepTemplateMedia>? StepTemplateMedia { get; set; }
    // Target time (seconds) from the step template, if available
    public int? StepTargetSeconds { get; set; }
    public string? StepTargetDisplay { get; set; }

    public async Task<IActionResult> OnGetAsync(int? stepExecId)
    {
        if (stepExecId == null)
        {
            return NotFound();
        }

        var step = await _context.StepExecs
            .AsNoTracking()
            .AsSplitQuery()
            .Include(s => s.PhaseExec)
                .ThenInclude(p => p.BuildExecution)
                    .ThenInclude(e => e.BuildOrder)
            .Include(s => s.Evidence)
                .ThenInclude(e => e.Media)
            .Include(s => s.Runs)
                .ThenInclude(r => r.Workstation)
            .Include(s => s.Tools)
            .Include(s => s.Materials)
            .FirstOrDefaultAsync(s => s.Id == stepExecId);

        if (step == null)
        {
            return NotFound();
        }

        StepExec = step;
        BuildOrder = step.PhaseExec.BuildExecution.BuildOrder;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        // CTO issue #43: in multi-user mode, show only the current user's running StepRun.
        CurrentRun = step.Runs.FirstOrDefault(r => r.FinishedAt == null && r.UserId == userId);
        if (CurrentRun != null)
        {
            var startedAtUtc = DateTime.SpecifyKind(CurrentRun.StartedAt, DateTimeKind.Utc);
            CurrentRunStartedAtEpochMs = new DateTimeOffset(startedAtUtc).ToUnixTimeMilliseconds();
        }

        // Get step template media by finding the template and matching step by sort order
        var buildOrder = step.PhaseExec.BuildExecution.BuildOrder;
        var template = await _context.ProcessTemplates
            .AsNoTracking()
            .AsSplitQuery()
            .Include(pt => pt.Phases)
                .ThenInclude(p => p.Steps)
                    .ThenInclude(s => s.Media)
                        .ThenInclude(sm => sm.Media)
            .FirstOrDefaultAsync(pt => pt.MachineModelId == buildOrder.MachineModelId 
                && pt.MachineVariantId == buildOrder.MachineVariantId);

        if (template != null)
        {
            var phaseTemplate = template.Phases.FirstOrDefault(p => p.SortOrder == step.PhaseExec.SortOrder);
            if (phaseTemplate != null)
            {
                var stepTemplate = phaseTemplate.Steps.FirstOrDefault(s => s.SortOrder == step.SortOrder);
                if (stepTemplate != null)
                {
                    StepTemplateMedia = stepTemplate.Media.ToList();
                        StepTargetSeconds = stepTemplate.TargetSeconds;
                        if (stepTemplate.TargetSeconds.HasValue)
                        {
                            var ts = TimeSpan.FromSeconds(stepTemplate.TargetSeconds.Value);
                            StepTargetDisplay = ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
                        }
                }
            }
        }

        return Page();
    }
}
