using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Execution;

namespace ManufacturingTimeTracking.Pages;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public IndexModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public int TotalOrders { get; set; }
    public int ActiveOrders { get; set; }
    public int TotalTemplates { get; set; }
    public int TotalModels { get; set; }

    /// <summary>When the user has a task in progress, the task id for quick link; otherwise null.</summary>
    public int? CurrentTaskId { get; set; }
    /// <summary>Title of the task the user is currently working on (when CurrentTaskId is set).</summary>
    public string? CurrentTaskTitle { get; set; }

    /// <summary>When the user has an order step in progress, the stepExec id for quick link; otherwise null.</summary>
    public int? CurrentStepExecId { get; set; }
    /// <summary>Title of the step the user is currently working on (when CurrentStepExecId is set).</summary>
    public string? CurrentStepTitle { get; set; }

    public async Task OnGetAsync()
    {
        TotalOrders = await _context.BuildOrders.CountAsync();
        ActiveOrders = await _context.BuildOrders.CountAsync(o => o.Status == "InProgress");
        TotalTemplates = await _context.ProcessTemplates.CountAsync();
        TotalModels = await _context.MachineModels.CountAsync(m => m.Active);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
        {
            // Prefer internal task in progress, if any
            // Prefer a running (not paused) session; if none, fall back to most recent paused session.
            var activeSession = await _context.TaskSessions
                .Include(s => s.Task)
                .Include(s => s.Pauses)
                .Where(s => s.UserId == userId && s.EndedAtUtc == null && !s.Pauses.Any(p => p.EndUtc == null))
                .OrderByDescending(s => s.StartedAtUtc)
                .FirstOrDefaultAsync();
            if (activeSession == null)
            {
                activeSession = await _context.TaskSessions
                    .Include(s => s.Task)
                    .Include(s => s.Pauses)
                    .Where(s => s.UserId == userId && s.EndedAtUtc == null)
                    .OrderByDescending(s => s.StartedAtUtc)
                    .FirstOrDefaultAsync();
            }
            if (activeSession?.Task != null)
            {
                CurrentTaskId = activeSession.Task.Id;
                CurrentTaskTitle = activeSession.Task.Title;
            }

            // If no internal task, fall back to a running order step for this user
            if (!CurrentTaskId.HasValue)
            {
                var runningStep = await _context.StepRuns
                    .Include(r => r.StepExec)
                    .OrderByDescending(r => r.StartedAt)
                    .FirstOrDefaultAsync(r => r.UserId == userId && r.FinishedAt == null);
                if (runningStep?.StepExec != null)
                {
                    CurrentStepExecId = runningStep.StepExecId;
                    CurrentStepTitle = runningStep.StepExec.Title;
                }
            }
        }
    }
}
