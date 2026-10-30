using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Execution;
using ManufacturingTimeTracking.Models.Templates;
using ManufacturingTimeTracking.Models.Catalog;
using MediaModel = ManufacturingTimeTracking.Models.Templates.Media;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using ManufacturingTimeTracking.Models;

namespace ManufacturingTimeTracking.Pages.Orders;

[Authorize]
public class ExecuteModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly UserManager<ApplicationUser> _userManager;

    public ExecuteModel(ApplicationDbContext context, IWebHostEnvironment environment, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _environment = environment;
        _userManager = userManager;
    }

    public BuildOrder BuildOrder { get; set; } = default!;
    public BuildExecution? CurrentExecution { get; set; }
    public string MachineModelName { get; set; } = "";
    public string MachineVariantName { get; set; } = "";
    public List<ProductionInterruption> ActiveInterruptions { get; set; } = new();
    /// <summary>Display names for users that have step runs in this execution.</summary>
    public Dictionary<string, string> StepRunUserDisplayNames { get; set; } = new();

    /// <summary>Step status filter: All, Pending, InProgress, Paused, Completed.</summary>
    public string StepFilter { get; set; } = "All";
    // When true show steps marked Hidden in the UI
    public bool ShowHidden { get; set; } = false;
    // Map of StepExec.Id -> whether its originating StepTemplate is hidden
    public Dictionary<int, bool> StepExecTemplateHidden { get; set; } = new();

    public bool IsStepHidden(StepExec? step)
    {
        if (step == null) return false;
        if (StepExecTemplateHidden.TryGetValue(step.Id, out var tplHidden))
            return tplHidden || step.Hidden;
        return step.Hidden;
    }

    private async Task<bool> IsUserClockedInForWorkAsync(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        var entries = await _context.TimeTrackingEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.TimestampUtc)
            .Take(200)
            .ToListAsync();

        var last = entries.FirstOrDefault();
        if (last == null) return false;
        if (last.ActionType == ManufacturingTimeTracking.Pages.TimeTracking.IndexModel.ActionClockOut) return false;
        if (last.ActionType == ManufacturingTimeTracking.Pages.TimeTracking.IndexModel.ActionBreakStart) return true;
        if (last.ActionType == ManufacturingTimeTracking.Pages.TimeTracking.IndexModel.ActionClockIn
            || last.ActionType == ManufacturingTimeTracking.Pages.TimeTracking.IndexModel.ActionBreakEnd) return true;
        return false;
    }

    private IActionResult RedirectWithClockInWarning(int buildOrderId)
    {
        TempData["StepsClockInRequired"] = "You need to clock in before starting a step. Clock in below, then start the step again.";
        return RedirectToPage("./Execute", new { id = buildOrderId, stepFilter = StepFilter });
    }

    public async Task<IActionResult> OnGetAsync(int? id, string? stepFilter, bool? showHidden)
    {
        if (id == null) return NotFound();

        var order = await _context.BuildOrders.FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();
        if (order.IsBlocked)
        {
            TempData["ToastrError"] = "This order is blocked. Unblock it before executing.";
            return RedirectToPage("./Details", new { id });
        }

        var model = await _context.MachineModels.FindAsync(order.MachineModelId);
        var variant = await _context.MachineVariants.FindAsync(order.MachineVariantId);
        MachineModelName = model?.Name ?? $"Model {order.MachineModelId}";
        MachineVariantName = variant?.Name ?? $"Variant {order.MachineVariantId}";

        var execution = await _context.BuildExecutions
            .Include(e => e.Phases)
                .ThenInclude(p => p.Steps)
                    .ThenInclude(s => s.Runs)
                        .ThenInclude(r => r.Workstation)
            .Include(e => e.Phases)
                .ThenInclude(p => p.Steps)
                    .ThenInclude(s => s.Evidence)
                        .ThenInclude(e => e.Media)
            .FirstOrDefaultAsync(e => e.BuildOrderId == id && e.Status == "InProgress");

        if (order == null)
        {
            return NotFound();
        }

        BuildOrder = order;
        CurrentExecution = execution;

        if (!string.IsNullOrEmpty(stepFilter) && (stepFilter == "All" || stepFilter == "Pending" || stepFilter == "Paused" || stepFilter == "InProgress" || stepFilter == "Completed"))
            StepFilter = stepFilter;

        // Accept showHidden query parameter
        ShowHidden = showHidden == true;

        // Preload display names for all users that have runs in this execution,
        // so the view can show who worked on each step.
        StepRunUserDisplayNames = new Dictionary<string, string>();
        if (execution != null)
        {
            var userIds = execution.Phases
                .SelectMany(p => p.Steps)
                .SelectMany(s => s.Runs)
                .Select(r => r.UserId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();

            foreach (var uid in userIds)
            {
                var user = await _userManager.FindByIdAsync(uid);
                var name = user?.Email ?? user?.UserName ?? uid;
                var at = name.IndexOf('@');
                if (at > 0) name = name[..at];
                StepRunUserDisplayNames[uid] = name;
            }

            // Preload template hidden flags for steps in this execution. Some executions may have been created
            // before the StepExec.Hidden property existed or before templates were updated, so consult the
            // originating StepTemplate.Hidden where available.
            var templateIds = execution.Phases.SelectMany(p => p.Steps).Select(s => s.StepTemplateId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
            if (templateIds.Any())
            {
                var tplMap = await _context.StepTemplates.AsNoTracking().Where(t => templateIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Hidden);
                foreach (var phase in execution.Phases)
                {
                    foreach (var step in phase.Steps)
                    {
                        if (step.StepTemplateId.HasValue && tplMap.TryGetValue(step.StepTemplateId.Value, out var hiddenFlag))
                        {
                            StepExecTemplateHidden[step.Id] = hiddenFlag;
                            // Also mark the StepExec.Hidden in-memory so existing view logic that checks s.Hidden will hide it
                            if (hiddenFlag)
                            {
                                step.Hidden = true;
                            }
                        }
                    }
                }
            }
            // Additionally, attempt to match steps to templates by title when StepTemplateId is missing
            // (covers older executions where StepTemplateId may be null).
            var modelId = order.MachineModelId;
            var variantId = order.MachineVariantId;
            var titleTemplates = await _context.StepTemplates.AsNoTracking()
                .Where(t => t.PhaseTemplate != null && t.PhaseTemplate.ProcessTemplate != null && t.PhaseTemplate.ProcessTemplate.MachineModelId == modelId && t.PhaseTemplate.ProcessTemplate.MachineVariantId == variantId)
                .ToListAsync();
            if (titleTemplates.Any())
            {
                var titleMap = titleTemplates.GroupBy(t => (t.Title ?? "").Trim()).ToDictionary(g => g.Key, g => g.First().Hidden);
                foreach (var phase in execution.Phases)
                {
                    foreach (var step in phase.Steps)
                    {
                        if (!step.StepTemplateId.HasValue)
                        {
                            var key = (step.Title ?? "").Trim();
                            if (!string.IsNullOrEmpty(key) && titleMap.TryGetValue(key, out var hiddenFlagByTitle))
                            {
                                StepExecTemplateHidden[step.Id] = hiddenFlagByTitle;
                                if (hiddenFlagByTitle)
                                    step.Hidden = true;
                            }
                        }
                    }
                }
            }
        }
        ActiveInterruptions = await _context.ProductionInterruptions
            .Where(pi => pi.BuildOrderId == order.Id && pi.EndTime == null)
            .OrderByDescending(pi => pi.StartTime)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostStartManufacturingAsync(int orderId)
    {
        var order = await _context.BuildOrders
            .Include(o => o.Executions)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
        {
            return NotFound();
        }
        if (order.IsBlocked)
        {
            TempData["ToastrError"] = "This order is blocked. Unblock it before starting.";
            return RedirectToPage("./Details", new { id = orderId });
        }

        // Check if there's already an active execution
        if (order.Executions.Any(e => e.Status == "InProgress"))
        {
            return RedirectToPage("./Execute", new { id = orderId });
        }

        // Find the template for this model/variant
        var template = await _context.ProcessTemplates
            .Include(pt => pt.Phases)
                .ThenInclude(p => p.Steps)
                    .ThenInclude(s => s.Tools)
                        .ThenInclude(st => st.Tool)
            .Include(pt => pt.Phases)
                .ThenInclude(p => p.Steps)
                    .ThenInclude(s => s.Materials)
                        .ThenInclude(sm => sm.Material)
            .FirstOrDefaultAsync(pt => pt.MachineModelId == order.MachineModelId 
                && pt.MachineVariantId == order.MachineVariantId);

        if (template == null)
        {
            TempData["Error"] = "No template found for this model and variant combination.";
            return RedirectToPage("./Execute", new { id = orderId });
        }

        // Create execution
        var execution = new BuildExecution
        {
            BuildOrderId = orderId,
            StartedAt = DateTime.UtcNow,
            Status = "InProgress"
        };
        _context.BuildExecutions.Add(execution);
        await _context.SaveChangesAsync();

        // Snapshot phases and steps
        foreach (var phaseTemplate in template.Phases.OrderBy(p => p.SortOrder))
        {
            var phaseExec = new PhaseExec
            {
                BuildExecutionId = execution.Id,
                SortOrder = phaseTemplate.SortOrder,
                Name = phaseTemplate.Name
            };
            _context.PhaseExecs.Add(phaseExec);
            await _context.SaveChangesAsync();

            foreach (var stepTemplate in phaseTemplate.Steps.OrderBy(s => s.SortOrder))
            {
                var stepExec = new StepExec
                {
                    PhaseExecId = phaseExec.Id,
                    SortOrder = stepTemplate.SortOrder,
                    Title = stepTemplate.Title ?? string.Empty,
                    Instructions = stepTemplate.Instructions ?? string.Empty,
                    AllowSkip = stepTemplate.AllowSkip,
                    StepTemplateId = stepTemplate.Id,
                    Hidden = stepTemplate.Hidden,
                    State = "Pending"
                };
                _context.StepExecs.Add(stepExec);
                await _context.SaveChangesAsync(); // Save to get stepExec.Id

                // Copy Tools from template
                foreach (var toolTemplate in stepTemplate.Tools)
                {
                    var stepExecTool = new StepExecTool
                    {
                        StepExecId = stepExec.Id,
                        ToolName = toolTemplate.Tool?.Name ?? "Unknown Tool"
                    };
                    _context.StepExecTools.Add(stepExecTool);
                }

                // Copy Materials from template
                foreach (var materialTemplate in stepTemplate.Materials)
                {
                    var stepExecMaterial = new StepExecMaterial
                    {
                        StepExecId = stepExec.Id,
                        MaterialName = materialTemplate.Material?.Name ?? "Unknown Material",
                        Qty = materialTemplate.Qty,
                        Unit = materialTemplate.Material?.Unit
                    };
                    _context.StepExecMaterials.Add(stepExecMaterial);
                }
                
                // Save tools and materials for this step
                await _context.SaveChangesAsync();
            }
        }

        order.Status = "InProgress";
        await _context.SaveChangesAsync();

        return RedirectToPage("./Execute", new { id = orderId });
    }

    public async Task<IActionResult> OnPostStartStepAsync(int stepExecId)
    {
        var step = await _context.StepExecs
            .Include(s => s.PhaseExec)
                .ThenInclude(p => p.BuildExecution)
            .FirstOrDefaultAsync(s => s.Id == stepExecId);

        if (step == null)
        {
            return NotFound();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var buildOrderId = step.PhaseExec.BuildExecution.BuildOrderId;
        if (!await IsUserClockedInForWorkAsync(userId))
            return RedirectWithClockInWarning(buildOrderId);

        // CTO issue #43: allow different users to work on the same step simultaneously.
        // Only prevent duplicate running step for the SAME user.
        var runningStep = await _context.StepRuns
            .FirstOrDefaultAsync(sr => sr.StepExecId == stepExecId && sr.FinishedAt == null && sr.UserId == userId);

        if (runningStep != null)
            return RedirectToPage("./Execute", new { id = step.PhaseExec.BuildExecution.BuildOrderId });

        // Disallow starting skipped steps. Everything else supports multi-user starts/resume.
        if (step.State == "Skipped")
            return RedirectToPage("./Execute", new { id = step.PhaseExec.BuildExecution.BuildOrderId });

        var conflictRun = await _context.StepRuns
            .Include(r => r.StepExec)
            .FirstOrDefaultAsync(r => r.UserId == userId && r.FinishedAt == null && r.StepExecId != stepExecId);
        if (conflictRun != null)
        {
            TempData["ConflictStepTitle"] = conflictRun.StepExec?.Title ?? "a task";
            TempData["ConflictStepExecId"] = conflictRun.StepExecId.ToString();
            TempData["PendingStepExecId"] = stepExecId.ToString();
            return RedirectToPage("./Execute", new { id = step.PhaseExec.BuildExecution.BuildOrderId });
        }

        var orderId = step.PhaseExec.BuildExecution.BuildOrderId;
        if (step.State == "Paused")
        {
            var activePaused = await _context.ProductionInterruptions
                .Where(pi => pi.BuildOrderId == orderId && pi.Category == ProductionInterruptionCategory.Paused && pi.EndTime == null)
                .OrderByDescending(pi => pi.StartTime)
                .FirstOrDefaultAsync();
            if (activePaused != null)
            {
                activePaused.EndTime = DateTime.UtcNow;
            }
        }

        var stepRun = new StepRun
        {
            StepExecId = stepExecId,
            StartedAt = DateTime.UtcNow,
            UserId = userId ?? ""
        };
        _context.StepRuns.Add(stepRun);

        step.State = "InProgress";
        await _context.SaveChangesAsync();

        return RedirectToPage("./StepView", new { stepExecId = stepExecId });
    }

    /// <summary>Pause the conflicting step and start the new one.</summary>
    public async Task<IActionResult> OnPostPauseConflictAndStartAsync(int conflictStepExecId, int pendingStepExecId, string notes)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var pendingStep = await _context.StepExecs
            .Include(s => s.PhaseExec)
                .ThenInclude(p => p.BuildExecution)
            .FirstOrDefaultAsync(s => s.Id == pendingStepExecId);
        if (pendingStep == null) return NotFound();
        if (!await IsUserClockedInForWorkAsync(userId))
            return RedirectWithClockInWarning(pendingStep.PhaseExec.BuildExecution.BuildOrderId);

        var conflictRun = await _context.StepRuns
            .Include(r => r.StepExec).ThenInclude(s => s.PhaseExec).ThenInclude(p => p.BuildExecution)
            .FirstOrDefaultAsync(r => r.StepExecId == conflictStepExecId && r.FinishedAt == null && r.UserId == userId);
        if (conflictRun != null)
        {
            conflictRun.FinishedAt = DateTime.UtcNow;
            conflictRun.DurationSeconds = (int)(conflictRun.FinishedAt.Value - conflictRun.StartedAt).TotalSeconds;
            var conflictStep = conflictRun.StepExec;

            // Persist ending this user's run before checking if others are still running.
            await _context.SaveChangesAsync();

            var anyOtherOpenRuns = await _context.StepRuns
                .AnyAsync(r => r.StepExecId == conflictStepExecId && r.FinishedAt == null);

            if (conflictStep != null)
                conflictStep.State = anyOtherOpenRuns ? "InProgress" : "Paused";

            var orderId = conflictStep?.PhaseExec?.BuildExecution?.BuildOrderId ?? 0;
            _context.ProductionInterruptions.Add(new ProductionInterruption
            {
                BuildOrderId = orderId,
                Category = ProductionInterruptionCategory.Paused,
                StartTime = DateTime.UtcNow,
                // Only keep as an active interruption when the whole step is paused.
                EndTime = anyOtherOpenRuns ? DateTime.UtcNow : null,
                Notes = string.IsNullOrWhiteSpace(notes) ? "Paused to start another task" : notes.Trim(),
                CreatedByUserId = userId
            });

            await _context.SaveChangesAsync();
        }

        return await StartStepInternalAsync(pendingStepExecId, userId, null);
    }

    /// <summary>Complete the conflicting step and start the new one.</summary>
    public async Task<IActionResult> OnPostCompleteConflictAndStartAsync(int conflictStepExecId, int pendingStepExecId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var pendingStep = await _context.StepExecs
            .Include(s => s.PhaseExec)
                .ThenInclude(p => p.BuildExecution)
            .FirstOrDefaultAsync(s => s.Id == pendingStepExecId);
        if (pendingStep == null) return NotFound();
        if (!await IsUserClockedInForWorkAsync(userId))
            return RedirectWithClockInWarning(pendingStep.PhaseExec.BuildExecution.BuildOrderId);

        var conflictRun = await _context.StepRuns
            .Include(r => r.StepExec).ThenInclude(s => s.PhaseExec).ThenInclude(p => p.BuildExecution)
            .FirstOrDefaultAsync(r => r.StepExecId == conflictStepExecId && r.FinishedAt == null && r.UserId == userId);
        if (conflictRun != null)
        {
            conflictRun.FinishedAt = DateTime.UtcNow;
            conflictRun.DurationSeconds = (int)(conflictRun.FinishedAt.Value - conflictRun.StartedAt).TotalSeconds;

            await _context.SaveChangesAsync();

            var anyOtherOpenRuns = await _context.StepRuns
                .AnyAsync(r => r.StepExecId == conflictStepExecId && r.FinishedAt == null);

            if (conflictRun.StepExec != null)
                conflictRun.StepExec.State = anyOtherOpenRuns ? "InProgress" : "Done";

            await _context.SaveChangesAsync();
        }

        return await StartStepInternalAsync(pendingStepExecId, userId, null);
    }

    private async Task<IActionResult> StartStepInternalAsync(int stepExecId, string userId, int? workstationId)
    {
        var step = await _context.StepExecs
            .Include(s => s.PhaseExec).ThenInclude(p => p.BuildExecution)
            .FirstOrDefaultAsync(s => s.Id == stepExecId);
        if (step == null) return NotFound();

        var orderId = step.PhaseExec.BuildExecution.BuildOrderId;
        if (!await IsUserClockedInForWorkAsync(userId))
            return RedirectWithClockInWarning(orderId);

        if (step.State == "Paused")
        {
            var activePaused = await _context.ProductionInterruptions
                .Where(pi => pi.BuildOrderId == orderId && pi.Category == ProductionInterruptionCategory.Paused && pi.EndTime == null)
                .OrderByDescending(pi => pi.StartTime)
                .FirstOrDefaultAsync();
            if (activePaused != null) activePaused.EndTime = DateTime.UtcNow;
        }

        var stepRun = new StepRun
        {
            StepExecId = stepExecId,
            StartedAt = DateTime.UtcNow,
            UserId = userId,
            WorkstationId = workstationId
        };
        _context.StepRuns.Add(stepRun);
        step.State = "InProgress";
        await _context.SaveChangesAsync();

        return RedirectToPage("./StepView", new { stepExecId });
    }

    /// <summary>Pause the current step run without completing the step. User can resume later.</summary>
    public async Task<IActionResult> OnPostPauseStepAsync(int stepExecId)
    {
        TempData["ToastrError"] = "You must log an interruption before pausing. Use the Pause button and complete the interruption form.";
        return RedirectToPage("./Execute", new { id = (await _context.StepExecs.Include(s => s.PhaseExec).ThenInclude(p => p.BuildExecution).FirstOrDefaultAsync(s => s.Id == stepExecId))?.PhaseExec?.BuildExecution?.BuildOrderId });
    }

    /// <summary>Log interruption (mandatory) then pause step (issue #10).</summary>
    public async Task<IActionResult> OnPostLogInterruptionAndPauseStepAsync(int stepExecId, string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            TempData["ToastrError"] = "Interruption notes are required.";
            var stepForRedirect = await _context.StepExecs.Include(s => s.PhaseExec).ThenInclude(p => p.BuildExecution).FirstOrDefaultAsync(s => s.Id == stepExecId);
            return RedirectToPage("./Execute", new { id = stepForRedirect?.PhaseExec?.BuildExecution?.BuildOrderId });
        }

        var step = await _context.StepExecs
            .Include(s => s.PhaseExec)
                .ThenInclude(p => p.BuildExecution)
            .FirstOrDefaultAsync(s => s.Id == stepExecId);

        if (step == null) return NotFound();

        var orderId = step.PhaseExec.BuildExecution.BuildOrderId;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        // CTO issue #43: pause is scoped to the current user's active StepRun.
        var runningStep = await _context.StepRuns
            .FirstOrDefaultAsync(sr => sr.StepExecId == stepExecId && sr.FinishedAt == null && sr.UserId == userId);
        if (runningStep != null)
        {
            runningStep.FinishedAt = DateTime.UtcNow;
            runningStep.DurationSeconds = (int)(runningStep.FinishedAt.Value - runningStep.StartedAt).TotalSeconds;
            _context.StepRuns.Update(runningStep);

            // Persist ended run first so the "any other open runs" check is correct.
            await _context.SaveChangesAsync();

            var anyOtherOpenRuns = await _context.StepRuns
                .AnyAsync(sr => sr.StepExecId == stepExecId && sr.FinishedAt == null);

            step.State = anyOtherOpenRuns ? "InProgress" : "Paused";

            _context.ProductionInterruptions.Add(new ProductionInterruption
            {
                BuildOrderId = orderId,
                Category = ProductionInterruptionCategory.Paused,
                StartTime = DateTime.UtcNow,
                // Only keep as an "active" interruption when the whole step is paused.
                EndTime = anyOtherOpenRuns ? DateTime.UtcNow : null,
                Notes = notes.Trim(),
                CreatedByUserId = userId
            });

            await _context.SaveChangesAsync();
        }
        else
        {
            TempData["ToastrError"] = "No active run for this user on this step.";
        }

        return RedirectToPage("./Execute", new { id = orderId });
    }

    /// <summary>Log interruption (mandatory) then stop/complete step (issue #10).</summary>
    public async Task<IActionResult> OnPostLogInterruptionAndStopStepAsync(int stepExecId, string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            TempData["ToastrError"] = "Interruption notes are required.";
            var stepForRedirect = await _context.StepExecs.Include(s => s.PhaseExec).ThenInclude(p => p.BuildExecution).FirstOrDefaultAsync(s => s.Id == stepExecId);
            return RedirectToPage("./Execute", new { id = stepForRedirect?.PhaseExec?.BuildExecution?.BuildOrderId });
        }

        var step = await _context.StepExecs
            .Include(s => s.PhaseExec)
                .ThenInclude(p => p.BuildExecution)
            .FirstOrDefaultAsync(s => s.Id == stepExecId);

        if (step == null) return NotFound();

        var orderId = step.PhaseExec.BuildExecution.BuildOrderId;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        _context.ProductionInterruptions.Add(new ProductionInterruption
        {
            BuildOrderId = orderId,
            Category = ProductionInterruptionCategory.StepStopped,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
            Notes = notes.Trim(),
            CreatedByUserId = userId
        });

        var runningStep = await _context.StepRuns
            .FirstOrDefaultAsync(sr => sr.StepExecId == stepExecId && sr.FinishedAt == null && sr.UserId == userId);
        if (runningStep != null)
        {
            runningStep.FinishedAt = DateTime.UtcNow;
            runningStep.DurationSeconds = (int)(runningStep.FinishedAt.Value - runningStep.StartedAt).TotalSeconds;
            _context.StepRuns.Update(runningStep);
        }
        else
        {
            TempData["ToastrError"] = "No active run for this user on this step.";
            return RedirectToPage("./Execute", new { id = orderId });
        }

        await _context.SaveChangesAsync();

        var anyOtherOpenRuns = await _context.StepRuns
            .AnyAsync(sr => sr.StepExecId == stepExecId && sr.FinishedAt == null);

        step.State = anyOtherOpenRuns ? "InProgress" : "Done";
        await _context.SaveChangesAsync();

        return RedirectToPage("./Execute", new { id = orderId });
    }

    public async Task<IActionResult> OnPostStopStepAsync(int stepExecId)
    {
        var step = await _context.StepExecs
            .Include(s => s.PhaseExec)
                .ThenInclude(p => p.BuildExecution)
            .FirstOrDefaultAsync(s => s.Id == stepExecId);
        if (step == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        var runningStep = await _context.StepRuns
            .FirstOrDefaultAsync(sr => sr.StepExecId == stepExecId && sr.FinishedAt == null && sr.UserId == userId);
        if (runningStep != null)
        {
            runningStep.FinishedAt = DateTime.UtcNow;
            runningStep.DurationSeconds = (int)(runningStep.FinishedAt.Value - runningStep.StartedAt).TotalSeconds;
            _context.StepRuns.Update(runningStep);
        }
        else
        {
            TempData["ToastrError"] = "No active run for this user on this step.";
            return RedirectToPage("./Execute", new { id = step.PhaseExec.BuildExecution.BuildOrderId });
        }

        await _context.SaveChangesAsync();

        var anyOtherOpenRuns = await _context.StepRuns
            .AnyAsync(sr => sr.StepExecId == stepExecId && sr.FinishedAt == null);

        step.State = anyOtherOpenRuns ? "InProgress" : "Done";
        await _context.SaveChangesAsync();

        return RedirectToPage("./Execute", new { id = step.PhaseExec.BuildExecution.BuildOrderId });
    }

    public async Task<IActionResult> OnPostReopenStepAsync(int stepExecId, string reopenReason)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Supervisor") && !User.IsInRole("Operator"))
            return Forbid();

        var step = await _context.StepExecs
            .Include(s => s.PhaseExec)
                .ThenInclude(p => p.BuildExecution)
            .FirstOrDefaultAsync(s => s.Id == stepExecId);

        if (step == null) return NotFound();

        var buildOrderId = step.PhaseExec.BuildExecution.BuildOrderId;

        // Record a production interruption entry for audit ("reopened step").
        if (string.IsNullOrWhiteSpace(reopenReason))
        {
            TempData["ToastrError"] = "Reopen reason is required.";
            return RedirectToPage("./Execute", new { id = buildOrderId });
        }
        var reason = reopenReason.Trim();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        _context.ProductionInterruptions.Add(new ProductionInterruption
        {
            BuildOrderId = buildOrderId,
            StepExecId = stepExecId,
            Category = ProductionInterruptionCategory.Reopened,
            StartTime = DateTime.UtcNow,
            EndTime = null,
            Notes = reason,
            CreatedByUserId = userId
        });

        step.State = "Pending";
        await _context.SaveChangesAsync();

        TempData["ToastrSuccess"] = "Step reopened. You can start it again.";

        return RedirectToPage("./Execute", new { id = step.PhaseExec.BuildExecution.BuildOrderId });
    }

    public async Task<IActionResult> OnPostSkipStepAsync(int stepExecId, string skipReason)
    {
        var step = await _context.StepExecs
            .Include(s => s.PhaseExec)
                .ThenInclude(p => p.BuildExecution)
            .FirstOrDefaultAsync(s => s.Id == stepExecId);

        if (step == null)
        {
            return NotFound();
        }

        if (!step.AllowSkip)
        {
            TempData["Error"] = "This step cannot be skipped.";
            return RedirectToPage("./Execute", new { id = step.PhaseExec.BuildExecution.BuildOrderId });
        }

        step.State = "Skipped";
        step.SkipReason = skipReason;
        await _context.SaveChangesAsync();

        return RedirectToPage("./Execute", new { id = step.PhaseExec.BuildExecution.BuildOrderId });
    }

    /// <summary>Reopen a skipped step so it can be executed (e.g. if skipped by mistake).</summary>
    public async Task<IActionResult> OnPostRestartStepAsync(int stepExecId)
    {
        var step = await _context.StepExecs
            .Include(s => s.PhaseExec)
                .ThenInclude(p => p.BuildExecution)
            .FirstOrDefaultAsync(s => s.Id == stepExecId);

        if (step == null)
        {
            return NotFound();
        }

        if (step.State != "Skipped")
        {
            return RedirectToPage("./Execute", new { id = step.PhaseExec.BuildExecution.BuildOrderId });
        }

        step.State = "Pending";
        step.SkipReason = null;
        await _context.SaveChangesAsync();

        TempData["ToastrSuccess"] = "Step reopened. You can start it again.";
        return RedirectToPage("./Execute", new { id = step.PhaseExec.BuildExecution.BuildOrderId });
    }

    public async Task<IActionResult> OnPostStartStepWithWorkstationAsync(int stepExecId, int? workstationId)
    {
        var step = await _context.StepExecs
            .Include(s => s.PhaseExec)
                .ThenInclude(p => p.BuildExecution)
            .FirstOrDefaultAsync(s => s.Id == stepExecId);

        if (step == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var buildOrderId = step.PhaseExec.BuildExecution.BuildOrderId;
        if (!await IsUserClockedInForWorkAsync(userId))
            return RedirectWithClockInWarning(buildOrderId);

        // Allow start when Pending, InProgress (another user is working), Done (rework), or Paused (resume).
        if (step.State == "Skipped")
            return RedirectToPage("./Execute", new { id = step.PhaseExec.BuildExecution.BuildOrderId });

        // Only prevent duplicate running step for the SAME user; multiple users
        // can work on the same step simultaneously (separate StepRun rows).
        var runningStep = await _context.StepRuns
            .FirstOrDefaultAsync(sr => sr.StepExecId == stepExecId && sr.FinishedAt == null && sr.UserId == userId);

        if (runningStep != null)
            return RedirectToPage("./Execute", new { id = step.PhaseExec.BuildExecution.BuildOrderId });

        var conflictRun = await _context.StepRuns
            .Include(r => r.StepExec)
            .FirstOrDefaultAsync(r => r.UserId == userId && r.FinishedAt == null && r.StepExecId != stepExecId);
        if (conflictRun != null)
        {
            TempData["ConflictStepTitle"] = conflictRun.StepExec?.Title ?? "a task";
            TempData["ConflictStepExecId"] = conflictRun.StepExecId.ToString();
            TempData["PendingStepExecId"] = stepExecId.ToString();
            return RedirectToPage("./Execute", new { id = step.PhaseExec.BuildExecution.BuildOrderId });
        }

        var orderId = step.PhaseExec.BuildExecution.BuildOrderId;
        if (step.State == "Paused")
        {
            var activePaused = await _context.ProductionInterruptions
                .Where(pi => pi.BuildOrderId == orderId && pi.Category == ProductionInterruptionCategory.Paused && pi.EndTime == null)
                .OrderByDescending(pi => pi.StartTime)
                .FirstOrDefaultAsync();
            if (activePaused != null)
            {
                activePaused.EndTime = DateTime.UtcNow;
            }
        }

        var stepRun = new StepRun
        {
            StepExecId = stepExecId,
            StartedAt = DateTime.UtcNow,
            UserId = userId ?? "",
            WorkstationId = workstationId
        };
        _context.StepRuns.Add(stepRun);

        step.State = "InProgress";
        await _context.SaveChangesAsync();

        // Redirect to full-screen step view
        return RedirectToPage("./StepView", new { stepExecId = stepExecId });
    }

    public async Task<IActionResult> OnPostUploadEvidenceAsync(int stepExecId, IFormFile? imageFile, string? note)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Supervisor") && !User.IsInRole("Operator"))
        {
            return Forbid();
        }

        var step = await _context.StepExecs
            .Include(s => s.PhaseExec)
                .ThenInclude(p => p.BuildExecution)
            .FirstOrDefaultAsync(s => s.Id == stepExecId);

        if (step == null)
        {
            return NotFound();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var evidencePath = configuration["MediaStorage:EvidencePath"] ?? "evidence";
        // Save under WebRootPath so files are served by UseStaticFiles (avoids 404 when running from VS)
        var uploadsRoot = Path.Combine(_environment.WebRootPath ?? "", "uploads");
        var uploadPath = Path.Combine(uploadsRoot, evidencePath, step.PhaseExec.BuildExecution.BuildOrderId.ToString(), stepExecId.ToString());

        if (!Directory.Exists(uploadPath))
        {
            Directory.CreateDirectory(uploadPath);
        }

        if (imageFile != null && imageFile.Length > 0)
        {
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(imageFile.FileName)}";
            var filePath = Path.Combine(uploadPath, fileName);
            var relativePath = Path.Combine(evidencePath, step.PhaseExec.BuildExecution.BuildOrderId.ToString(), stepExecId.ToString(), fileName).Replace('\\', '/');

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await imageFile.CopyToAsync(stream);
            }

            // Determine media type based on file extension
            var extension = Path.GetExtension(imageFile.FileName).ToLower();
            var mediaType = (extension == ".mp4" || extension == ".avi" || extension == ".mov" || extension == ".wmv" || extension == ".flv" || extension == ".webm") 
                ? "Video" 
                : "Image";

            var media = new MediaModel
            {
                Type = mediaType,
                UrlOrPath = relativePath,
                UploadedBy = userId ?? "",
                UploadedAt = DateTime.UtcNow
            };
            _context.Media.Add(media);
            await _context.SaveChangesAsync();

            // Create instruction (reference material for future users)
            var evidence = new StepEvidence
            {
                StepExecId = stepExecId,
                MediaId = media.Id,
                Note = note,
                UserId = userId ?? "",
                CreatedAt = DateTime.UtcNow
            };
            _context.StepEvidence.Add(evidence);
            await _context.SaveChangesAsync();

            await SyncInstructionToTemplateAsync(step, media.Id, note);

            TempData["Success"] = "Instruction added successfully! This will help future users understand how to perform this step.";
        }
        else if (!string.IsNullOrWhiteSpace(note))
        {
            // Create a placeholder media entry for notes-only instructions
            var media = new MediaModel
            {
                Type = "Note",
                UrlOrPath = "",
                UploadedBy = userId ?? "",
                UploadedAt = DateTime.UtcNow
            };
            _context.Media.Add(media);
            await _context.SaveChangesAsync();

            // Create instruction (reference material for future users)
            var evidence = new StepEvidence
            {
                StepExecId = stepExecId,
                MediaId = media.Id,
                Note = note,
                UserId = userId ?? "",
                CreatedAt = DateTime.UtcNow
            };
            _context.StepEvidence.Add(evidence);
            await _context.SaveChangesAsync();

            await SyncInstructionToTemplateAsync(step, media.Id, note);

            TempData["Success"] = "Instruction added successfully! This will help future users understand how to perform this step.";
        }
        else
        {
            TempData["Error"] = "Please provide either an image/video or written instructions.";
        }

        return RedirectToPage("./Execute", new { id = step.PhaseExec.BuildExecution.BuildOrderId });
    }

    /// <summary>Sync an instruction (evidence) added on an order back to the process template so future orders get it.</summary>
    private async Task SyncInstructionToTemplateAsync(StepExec step, int mediaId, string? note)
    {
        var order = await _context.BuildOrders.FindAsync(step.PhaseExec.BuildExecution.BuildOrderId);
        if (order == null) return;
        var template = await _context.ProcessTemplates
            .Include(pt => pt.Phases)
            .ThenInclude(p => p.Steps)
            .FirstOrDefaultAsync(pt => pt.MachineModelId == order.MachineModelId && pt.MachineVariantId == order.MachineVariantId);
        var phaseTemplate = template?.Phases?.FirstOrDefault(p => p.SortOrder == step.PhaseExec.SortOrder);
        var stepTemplate = phaseTemplate?.Steps?.FirstOrDefault(s => s.SortOrder == step.SortOrder);
        if (stepTemplate != null)
        {
            _context.StepTemplateMedia.Add(new StepTemplateMedia
            {
                StepTemplateId = stepTemplate.Id,
                MediaId = mediaId,
                Caption = note
            });
            await _context.SaveChangesAsync();
        }
    }

    public async Task<IActionResult> OnPostAddStepAsync(int phaseExecId, string title, string instructions, bool allowSkip)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Supervisor") && !User.IsInRole("Operator"))
        {
            return Forbid();
        }

        var phase = await _context.PhaseExecs
            .Include(p => p.BuildExecution)
            .FirstOrDefaultAsync(p => p.Id == phaseExecId);

        if (phase == null)
        {
            return NotFound();
        }

        // Get the highest sort order in this phase
        var maxSortOrder = (await _context.StepExecs
            .Where(s => s.PhaseExecId == phaseExecId)
            .Select(s => (int?)s.SortOrder)
            .MaxAsync()) ?? 0;

        var newStep = new StepExec
        {
            PhaseExecId = phaseExecId,
            SortOrder = maxSortOrder + 1,
            Title = title ?? string.Empty,
            Instructions = instructions ?? string.Empty,
            AllowSkip = allowSkip,
            State = "Pending"
        };

        _context.StepExecs.Add(newStep);
        await _context.SaveChangesAsync();

        // Sync to template: add same step to the process template (CTO: steps added on order must appear on template)
        var order = await _context.BuildOrders.FindAsync(phase.BuildExecution.BuildOrderId);
        if (order != null)
        {
            var template = await _context.ProcessTemplates
                .Include(pt => pt.Phases)
                .FirstOrDefaultAsync(pt => pt.MachineModelId == order.MachineModelId && pt.MachineVariantId == order.MachineVariantId);
            var phaseTemplate = template?.Phases?.FirstOrDefault(p => p.SortOrder == phase.SortOrder);
            if (phaseTemplate != null)
            {
                var maxTemplateSort = await _context.StepTemplates
                    .Where(s => s.PhaseTemplateId == phaseTemplate.Id)
                    .Select(s => (int?)s.SortOrder)
                    .MaxAsync() ?? 0;
                _context.StepTemplates.Add(new StepTemplate
                {
                    PhaseTemplateId = phaseTemplate.Id,
                    SortOrder = maxTemplateSort + 1,
                    Title = newStep.Title,
                    Instructions = newStep.Instructions,
                    AllowSkip = newStep.AllowSkip
                });
                await _context.SaveChangesAsync();
            }
        }

        return RedirectToPage("./Execute", new { id = phase.BuildExecution.BuildOrderId });
    }

    /// <summary>Delete a step from the order (only if Pending) and from the template (CTO: sync delete to template).</summary>
    public async Task<IActionResult> OnPostDeleteStepFromOrderAsync(int stepExecId)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Supervisor") && !User.IsInRole("Operator"))
            return Forbid();

        var step = await _context.StepExecs
            .Include(s => s.PhaseExec)
                .ThenInclude(p => p.BuildExecution)
            .FirstOrDefaultAsync(s => s.Id == stepExecId);
        if (step == null) return NotFound();

        if (step.State != "Pending")
        {
            TempData["ToastrError"] = "Only steps that have not started can be removed.";
            return RedirectToPage("./Execute", new { id = step.PhaseExec.BuildExecution.BuildOrderId });
        }

        var phase = step.PhaseExec;
        var orderId = phase.BuildExecution.BuildOrderId;
        var phaseSortOrder = phase.SortOrder;
        var stepSortOrder = step.SortOrder;

        _context.StepExecs.Remove(step);
        var laterOrderSteps = await _context.StepExecs
            .Where(s => s.PhaseExecId == phase.Id && s.SortOrder > stepSortOrder)
            .ToListAsync();
        foreach (var s in laterOrderSteps)
            s.SortOrder--;

        var order = await _context.BuildOrders.FindAsync(orderId);
        if (order != null)
        {
            var template = await _context.ProcessTemplates
                .Include(pt => pt.Phases)
                .ThenInclude(p => p.Steps)
                .FirstOrDefaultAsync(pt => pt.MachineModelId == order.MachineModelId && pt.MachineVariantId == order.MachineVariantId);
            var phaseTemplate = template?.Phases?.FirstOrDefault(p => p.SortOrder == phaseSortOrder);
            var stepTemplate = phaseTemplate?.Steps?.FirstOrDefault(s => s.SortOrder == stepSortOrder);
            if (stepTemplate != null)
            {
                _context.StepTemplates.Remove(stepTemplate);
                var laterTemplateSteps = await _context.StepTemplates
                    .Where(s => s.PhaseTemplateId == phaseTemplate!.Id && s.SortOrder > stepSortOrder)
                    .ToListAsync();
                foreach (var s in laterTemplateSteps)
                    s.SortOrder--;
            }
        }

        await _context.SaveChangesAsync();
        TempData["Success"] = "Step removed from order and from template.";
        return RedirectToPage("./Execute", new { id = orderId });
    }
}
