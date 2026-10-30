using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Models.TimeTracking;
using ManufacturingTimeTracking.Services;

namespace ManufacturingTimeTracking.Pages.AI;

[Authorize(Policy = "CanUseTimeManagement")]
public class TechnicianReportModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly GptReportService _gpt;

    public TechnicianReportModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, GptReportService gpt)
    {
        _db = db;
        _userManager = userManager;
        _gpt = gpt;
    }

    // ── Filters ──────────────────────────────────────────────────────
    [BindProperty(SupportsGet = true)]
    public string? TechnicianId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    // ── Result ───────────────────────────────────────────────────────
    public string? ReportText { get; set; }
    public string? ErrorMessage { get; set; }
    public bool Generated { get; set; }
    public bool GptConfigured => _gpt.IsConfigured;

    // ── Dropdowns ────────────────────────────────────────────────────
    public SelectList? TechnicianList { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await PopulateDropdownsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await PopulateDropdownsAsync();

        if (string.IsNullOrWhiteSpace(TechnicianId))
        {
            ErrorMessage = "Please select a technician.";
            return Page();
        }

        var techUser = await _userManager.FindByIdAsync(TechnicianId);
        var techName = techUser?.UserName ?? techUser?.Email ?? TechnicianId;
        var at = techName.IndexOf('@');
        if (at > 0) techName = techName[..at];

        var from = FromDate ?? DateTime.UtcNow.Date.AddDays(-30);
        var to = (ToDate ?? DateTime.UtcNow.Date).AddDays(1);

        // ── Step runs for technician ──────────────────────────────────
        var stepRuns = await _db.StepRuns
            .Include(r => r.StepExec).ThenInclude(s => s!.PhaseExec).ThenInclude(p => p!.BuildExecution).ThenInclude(e => e!.BuildOrder)
            .AsNoTracking()
            .Where(r => r.UserId == TechnicianId
                && r.StartedAt >= from && r.StartedAt < to
                && r.StepExec != null && r.StepExec.PhaseExec != null && r.StepExec.PhaseExec.BuildExecution != null)
            .OrderBy(r => r.StartedAt)
            .ToListAsync();

        // ── Time tracking entries ─────────────────────────────────────
        var ttEntries = await _db.TimeTrackingEntries
            .AsNoTracking()
            .Where(e => e.UserId == TechnicianId && e.TimestampUtc >= from && e.TimestampUtc < to)
            .OrderBy(e => e.TimestampUtc)
            .ToListAsync();

        var breakReasonIds = ttEntries
            .Where(e => e.BreakReasonId.HasValue)
            .Select(e => e.BreakReasonId!.Value)
            .Distinct()
            .ToList();
        var breakReasonNames = await _db.BreakReasons
            .AsNoTracking()
            .Where(b => breakReasonIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name);

        var machineModels = await _db.MachineModels.AsNoTracking()
            .ToDictionaryAsync(m => m.Id, m => m.Name);

        // ── Format data ───────────────────────────────────────────────
        var sb = new StringBuilder();
        sb.AppendLine($"Technician: {techName}");
        sb.AppendLine($"Period: {from:yyyy-MM-dd} to {(to - TimeSpan.FromDays(1)):yyyy-MM-dd}");
        sb.AppendLine();

        // Step run summary
        sb.AppendLine($"Task activity:");
        sb.AppendLine($"  Total step runs: {stepRuns.Count}");
        var completedRuns = stepRuns.Where(r => r.FinishedAt.HasValue).ToList();
        var inProgressRuns = stepRuns.Where(r => !r.FinishedAt.HasValue).ToList();
        sb.AppendLine($"  Completed: {completedRuns.Count}, Still in-progress: {inProgressRuns.Count}");

        if (completedRuns.Any())
        {
            var avgSec = completedRuns.Average(r => r.DurationSeconds ?? 0);
            var maxSec = completedRuns.Max(r => r.DurationSeconds ?? 0);
            var minSec = completedRuns.Where(r => (r.DurationSeconds ?? 0) > 0).Min(r => r.DurationSeconds ?? 0);
            sb.AppendLine($"  Average step duration: {TimeSpan.FromSeconds(avgSec):hh\\:mm\\:ss}");
            sb.AppendLine($"  Longest step: {TimeSpan.FromSeconds(maxSec):hh\\:mm\\:ss}");
            sb.AppendLine($"  Shortest step: {TimeSpan.FromSeconds(minSec):hh\\:mm\\:ss}");

            var totalTaskSec = completedRuns.Sum(r => r.DurationSeconds ?? 0);
            sb.AppendLine($"  Total task time: {TimeSpan.FromSeconds(totalTaskSec):hh\\:mm\\:ss}");
        }
        sb.AppendLine();

        // Distinct orders / machines worked on
        var orderIds = stepRuns
            .Select(r => r.StepExec?.PhaseExec?.BuildExecution?.BuildOrderId ?? 0)
            .Where(id => id > 0).Distinct().ToList();
        var orders = await _db.BuildOrders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .ToListAsync();

        sb.AppendLine($"Orders involved: {orders.Count}");
        foreach (var o in orders.Take(20))
        {
            var modelName = machineModels.GetValueOrDefault(o.MachineModelId, "Unknown");
            sb.AppendLine($"  {o.ExternalRef} / {o.SerialNumber} — {modelName} — {o.Status} — Customer: {o.CustomerName}");
        }
        sb.AppendLine();

        // Step details (top 10 longest)
        if (completedRuns.Any())
        {
            sb.AppendLine("Top 10 longest steps:");
            foreach (var r in completedRuns.OrderByDescending(r => r.DurationSeconds ?? 0).Take(10))
                sb.AppendLine($"  '{r.StepExec?.Title ?? "—"}' — {TimeSpan.FromSeconds(r.DurationSeconds ?? 0):hh\\:mm\\:ss} — Started: {r.StartedAt:yyyy-MM-dd HH:mm}");
            sb.AppendLine();
        }

        // Clock in/out summary
        if (ttEntries.Any())
        {
            var (worked, breakTime, sessions, breaks) = ComputeTimeTracking(ttEntries);
            sb.AppendLine($"Clock-in/out summary (from time tracking system):");
            sb.AppendLine($"  Sessions (clock-in events): {sessions}");
            sb.AppendLine($"  Total work time (presence): {worked:hh\\:mm\\:ss}");
            sb.AppendLine($"  Total break time: {breakTime:hh\\:mm\\:ss}");
            sb.AppendLine($"  Effective work time: {(worked - breakTime):hh\\:mm\\:ss}");
            sb.AppendLine($"  Break count: {breaks}");

            var breaksByReason = ttEntries
                .Where(e => e.ActionType == "BREAK_START" && e.BreakReasonId.HasValue)
                .GroupBy(e => breakReasonNames.GetValueOrDefault(e.BreakReasonId!.Value, "Unknown"));
            foreach (var g in breaksByReason)
                sb.AppendLine($"  Break reason '{g.Key}': {g.Count()} time(s)");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("No clock-in/out records found for this period.");
            sb.AppendLine();
        }

        var dataText = sb.ToString();

        // ── Load prompt template ─────────────────────────────────────
        var promptRecord = await _db.AiPrompts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Name == "Prompt_Template_Technician_Analysis");

        if (promptRecord == null)
        {
            ErrorMessage = "Prompt template 'Prompt_Template_Technician_Analysis' not found in database. Please create it from the AI Prompts admin page.";
            return Page();
        }

        var finalPrompt = promptRecord.PromptText.Replace("{DATA}", dataText);

        // ── Call GPT ─────────────────────────────────────────────────
        var (result, error) = await _gpt.GenerateAsync(finalPrompt);
        if (error != null)
            ErrorMessage = error;
        else
        {
            ReportText = result;
            Generated = true;
        }

        return Page();
    }

    private static (TimeSpan worked, TimeSpan breakTime, int sessions, int breaks) ComputeTimeTracking(
        List<TimeTrackingEntry> ordered)
    {
        var worked = TimeSpan.Zero;
        var breakTime = TimeSpan.Zero;
        var sessions = 0;
        var breaks = 0;
        DateTime? clockInTime = null;
        DateTime? breakStartTime = null;

        foreach (var e in ordered)
        {
            switch (e.ActionType)
            {
                case "CLOCK_IN":
                    clockInTime = e.TimestampUtc;
                    sessions++;
                    break;
                case "CLOCK_OUT":
                    if (clockInTime.HasValue)
                    {
                        worked += e.TimestampUtc - clockInTime.Value;
                        if (breakStartTime.HasValue) { breakTime += e.TimestampUtc - breakStartTime.Value; breakStartTime = null; }
                    }
                    clockInTime = null;
                    break;
                case "BREAK_START":
                    breakStartTime = e.TimestampUtc;
                    breaks++;
                    break;
                case "BREAK_END":
                    if (breakStartTime.HasValue) { breakTime += e.TimestampUtc - breakStartTime.Value; breakStartTime = null; }
                    break;
            }
        }

        return (worked, breakTime, sessions, breaks);
    }

    private async Task PopulateDropdownsAsync()
    {
        var technicianIds = await _db.StepRuns
            .Where(r => !string.IsNullOrEmpty(r.UserId))
            .Select(r => r.UserId)
            .Distinct()
            .ToListAsync();

        var techItems = new List<SelectListItem> { new("— Select technician —", "") };
        foreach (var uid in technicianIds.OrderBy(x => x))
        {
            var u = await _userManager.FindByIdAsync(uid);
            var name = u?.UserName ?? u?.Email ?? uid;
            var at = name.IndexOf('@');
            techItems.Add(new SelectListItem(at > 0 ? name[..at] : name, uid));
        }
        TechnicianList = new SelectList(techItems, "Value", "Text", TechnicianId);
    }
}
