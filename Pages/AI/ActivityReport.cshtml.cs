using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Models.AI;
using ManufacturingTimeTracking.Services;

namespace ManufacturingTimeTracking.Pages.AI;

[Authorize(Policy = "CanUseTimeManagement")]
public class ActivityReportModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly GptReportService _gpt;
    private readonly ILogger<ActivityReportModel> _logger;

    public ActivityReportModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, GptReportService gpt, ILogger<ActivityReportModel> logger)
    {
        _db = db;
        _userManager = userManager;
        _gpt = gpt;
        _logger = logger;
    }

    // ── Filters ──────────────────────────────────────────────────────
    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TechnicianId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? MachineModelId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? CustomerName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? InterventionType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? UserIdFilter { get; set; }

    [BindProperty]
    public int SelectedPromptId { get; set; }

    [BindProperty]
    public string EditedPromptText { get; set; } = "";

    // ── Result ───────────────────────────────────────────────────────
    public string? ReportText { get; set; }
    public string? ErrorMessage { get; set; }
    public bool Generated { get; set; }
    public bool GptConfigured => _gpt.IsConfigured;
    public string? LastPrompt { get; set; }

    // ── Dropdowns ────────────────────────────────────────────────────
    public SelectList? TechnicianList { get; set; }
    public SelectList? MachineModelList { get; set; }
    public SelectList? PromptSelectList { get; set; }
    public Dictionary<int, string> PromptsData { get; set; } = new();

    public List<ActivityRow> Rows { get; set; } = new();

    public class ActivityRow
    {
        public int StepRunId { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime StartedAtLocal { get; set; }
        public string UserId { get; set; } = "";
        public string UserDisplayName { get; set; } = "";
        public string MachineModel { get; set; } = "";
        public string MachineVariant { get; set; } = "";
        public string OrderRef { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string StepTitle { get; set; } = "";
        public int? DurationSeconds { get; set; }
        public bool InProgress { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!FromDate.HasValue) FromDate = DateTime.UtcNow.Date;
        if (!ToDate.HasValue) ToDate = FromDate;

        await PopulateDropdownsAsync();
        await LoadPromptsAsync();
        await LoadActivityAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // ensure TechnicianId reflects the current user filter from the UI
        TechnicianId = UserIdFilter;
        await PopulateDropdownsAsync();

        var from = FromDate ?? DateTime.UtcNow.Date.AddDays(-30);
        var to = (ToDate ?? DateTime.UtcNow.Date).AddDays(1);

        // ── Gather data ──────────────────────────────────────────────
        var orderQuery = _db.BuildOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(CustomerName))
            orderQuery = orderQuery.Where(o => o.CustomerName.Contains(CustomerName));
        if (MachineModelId.HasValue)
            orderQuery = orderQuery.Where(o => o.MachineModelId == MachineModelId.Value);

        var orders = await orderQuery
            .Include(o => o.BlockedReason)
            .ToListAsync();

        var orderIds = orders.Select(o => o.Id).ToList();

        var stepRunQuery = _db.StepRuns
            .Include(r => r.StepExec).ThenInclude(s => s!.PhaseExec).ThenInclude(p => p!.BuildExecution)
            .AsNoTracking()
            .Where(r => r.StartedAt >= from && r.StartedAt < to
                && r.StepExec != null && r.StepExec.PhaseExec != null && r.StepExec.PhaseExec.BuildExecution != null
                && orderIds.Contains(r.StepExec.PhaseExec.BuildExecution.BuildOrderId));

        if (!string.IsNullOrWhiteSpace(TechnicianId))
            stepRunQuery = stepRunQuery.Where(r => r.UserId == TechnicianId);

        var stepRuns = await stepRunQuery.OrderBy(r => r.StartedAt).ToListAsync();

        var interruptions = await _db.ProductionInterruptions
            .AsNoTracking()
            .Where(i => orderIds.Contains(i.BuildOrderId))
            .ToListAsync();

        var machineModels = await _db.MachineModels.AsNoTracking()
            .ToDictionaryAsync(m => m.Id, m => m.Name);

        // ── Build display names ───────────────────────────────────────
        var userIds = stepRuns.Select(r => r.UserId).Where(u => !string.IsNullOrEmpty(u)).Distinct().ToList();
        var userNames = new Dictionary<string, string>();
        foreach (var uid in userIds)
        {
            var u = await _userManager.FindByIdAsync(uid);
            var display = u?.UserName ?? u?.Email ?? uid;
            var at = display.IndexOf('@');
            userNames[uid] = at > 0 ? display[..at] : display;
        }

        // ── Format data for prompt ────────────────────────────────────
        var sb = new StringBuilder();
        sb.AppendLine($"Period: {from:yyyy-MM-dd} to {(to - TimeSpan.FromDays(1)):yyyy-MM-dd}");
        sb.AppendLine($"Total orders in scope: {orders.Count}");
        sb.AppendLine($"Orders status breakdown: Pending={orders.Count(o => o.Status == "Pending")}, InProgress={orders.Count(o => o.Status == "InProgress")}, Completed={orders.Count(o => o.Status == "Completed")}, Cancelled={orders.Count(o => o.Status == "Cancelled")}");
        sb.AppendLine($"Blocked orders: {orders.Count(o => o.IsBlocked)}");
        sb.AppendLine();

        sb.AppendLine($"Step runs in period: {stepRuns.Count}");
        var completedRuns = stepRuns.Where(r => r.FinishedAt.HasValue).ToList();
        var inProgressRuns = stepRuns.Where(r => !r.FinishedAt.HasValue).ToList();
        sb.AppendLine($"  Completed: {completedRuns.Count}, In-Progress: {inProgressRuns.Count}");
        if (completedRuns.Any())
        {
            var avgSec = completedRuns.Average(r => r.DurationSeconds ?? 0);
            sb.AppendLine($"  Average step duration: {TimeSpan.FromSeconds(avgSec):hh\\:mm\\:ss}");
        }
        sb.AppendLine();

        // Per-technician summary
        sb.AppendLine("Technician summary:");
        foreach (var grp in stepRuns.GroupBy(r => r.UserId).OrderBy(g => g.Key))
        {
            var name = userNames.GetValueOrDefault(grp.Key, grp.Key);
            var totalSec = grp.Where(r => r.FinishedAt.HasValue).Sum(r => r.DurationSeconds ?? 0);
            sb.AppendLine($"  {name}: {grp.Count()} steps, total time {TimeSpan.FromSeconds(totalSec):hh\\:mm\\:ss}");
        }
        sb.AppendLine();

        // Per-machine summary (top 10)
        sb.AppendLine("Machine model activity (top 10 by step count):");
        var orderById = orders.ToDictionary(o => o.Id);
        var byModel = stepRuns
            .GroupBy(r =>
            {
                var orderId = r.StepExec?.PhaseExec?.BuildExecution?.BuildOrderId ?? 0;
                if (orderId > 0 && orderById.TryGetValue(orderId, out var ord))
                    return machineModels.GetValueOrDefault(ord.MachineModelId, "Unknown");
                return "Unknown";
            })
            .OrderByDescending(g => g.Count())
            .Take(10);
        foreach (var g in byModel)
            sb.AppendLine($"  {g.Key}: {g.Count()} steps");
        sb.AppendLine();

        // Interruptions
        if (interruptions.Any())
        {
            sb.AppendLine($"Production interruptions: {interruptions.Count}");
            var irTypes = interruptions.GroupBy(i => i.Category.ToString()).OrderByDescending(g => g.Count());
            foreach (var g in irTypes)
                sb.AppendLine($"  {g.Key}: {g.Count()}");
            sb.AppendLine();
        }

        // Blocked orders detail
        var blocked = orders.Where(o => o.IsBlocked).ToList();
        if (blocked.Any())
        {
            sb.AppendLine("Blocked orders detail:");
            foreach (var o in blocked.Take(20))
                sb.AppendLine($"  Order {o.ExternalRef} / {o.SerialNumber}: {o.BlockedReason?.Name ?? "No reason"} — {o.BlockedNotes ?? ""}");
            sb.AppendLine();
        }

        var dataText = sb.ToString();

        // ── Load prompt template ─────────────────────────────────────
        var promptRecord = await _db.AiPrompts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Name == "Prompt_Template_Report");

        if (promptRecord == null)
        {
            ErrorMessage = "Prompt template 'Prompt_Template_Report' not found in database. Please create it from the AI Prompts admin page.";
            return Page();
        }

        var finalPrompt = promptRecord.PromptText.Replace("{DATA}", dataText);
        LastPrompt = finalPrompt;
        try
        {
            _logger?.LogInformation("Sending finalPrompt to GPT (len={len})", finalPrompt?.Length ?? 0);
        }
        catch { }

        // ── Call GPT ─────────────────────────────────────────────────
        var (result, error) = await _gpt.GenerateAsync(finalPrompt);
        if (error != null)
        {
            ErrorMessage = error;
            _logger?.LogWarning("GPT error: {err}", error);
        }
        else
        {
            ReportText = result;
            Generated = true;
            _logger?.LogInformation("GPT result length: {len}", result?.Length ?? 0);
        }

        return Page();
    }

    private async Task<string> BuildDataTextAsync(DateTime from, DateTime to)
    {
        // Similar logic as in OnPostAsync to build the data summary used in prompts
        var orderQuery = _db.BuildOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(CustomerName))
            orderQuery = orderQuery.Where(o => o.CustomerName.Contains(CustomerName));
        if (MachineModelId.HasValue)
            orderQuery = orderQuery.Where(o => o.MachineModelId == MachineModelId.Value);

        var orders = await orderQuery
            .Include(o => o.BlockedReason)
            .ToListAsync();

        var orderIds = orders.Select(o => o.Id).ToList();

        var stepRunQuery = _db.StepRuns
            .Include(r => r.StepExec).ThenInclude(s => s!.PhaseExec).ThenInclude(p => p!.BuildExecution)
            .AsNoTracking()
            .Where(r => r.StartedAt >= from && r.StartedAt < to
                && r.StepExec != null && r.StepExec.PhaseExec != null && r.StepExec.PhaseExec.BuildExecution != null
                && orderIds.Contains(r.StepExec.PhaseExec.BuildExecution.BuildOrderId));

        if (!string.IsNullOrWhiteSpace(TechnicianId))
            stepRunQuery = stepRunQuery.Where(r => r.UserId == TechnicianId);

        var stepRuns = await stepRunQuery.OrderBy(r => r.StartedAt).ToListAsync();

        var interruptions = await _db.ProductionInterruptions
            .AsNoTracking()
            .Where(i => orderIds.Contains(i.BuildOrderId))
            .ToListAsync();

        var machineModels = await _db.MachineModels.AsNoTracking()
            .ToDictionaryAsync(m => m.Id, m => m.Name);

        // Build display names
        var userIds = stepRuns.Select(r => r.UserId).Where(u => !string.IsNullOrEmpty(u)).Distinct().ToList();
        var userNames = new Dictionary<string, string>();
        foreach (var uid in userIds)
        {
            var u = await _userManager.FindByIdAsync(uid);
            var display = u?.UserName ?? u?.Email ?? uid;
            var at = display.IndexOf('@');
            userNames[uid] = at > 0 ? display[..at] : display;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Period: {from:yyyy-MM-dd} to {(to - TimeSpan.FromDays(1)):yyyy-MM-dd}");
        sb.AppendLine($"Total orders in scope: {orders.Count}");
        sb.AppendLine($"Orders status breakdown: Pending={orders.Count(o => o.Status == "Pending")}, InProgress={orders.Count(o => o.Status == "InProgress")}, Completed={orders.Count(o => o.Status == "Completed")}, Cancelled={orders.Count(o => o.Status == "Cancelled")} ");
        sb.AppendLine($"Blocked orders: {orders.Count(o => o.IsBlocked)}");
        sb.AppendLine();

        sb.AppendLine($"Step runs in period: {stepRuns.Count}");
        var completedRuns = stepRuns.Where(r => r.FinishedAt.HasValue).ToList();
        var inProgressRuns = stepRuns.Where(r => !r.FinishedAt.HasValue).ToList();
        sb.AppendLine($"  Completed: {completedRuns.Count}, In-Progress: {inProgressRuns.Count}");
        if (completedRuns.Any())
        {
            var avgSec = completedRuns.Average(r => r.DurationSeconds ?? 0);
            sb.AppendLine($"  Average step duration: {TimeSpan.FromSeconds(avgSec):hh\\:mm\\:ss}");
        }
        sb.AppendLine();

        sb.AppendLine("Technician summary:");
        foreach (var grp in stepRuns.GroupBy(r => r.UserId).OrderBy(g => g.Key))
        {
            var name = userNames.GetValueOrDefault(grp.Key, grp.Key);
            var totalSec = grp.Where(r => r.FinishedAt.HasValue).Sum(r => r.DurationSeconds ?? 0);
            sb.AppendLine($"  {name}: {grp.Count()} steps, total time {TimeSpan.FromSeconds(totalSec):hh\\:mm\\:ss}");
        }
        sb.AppendLine();

        var orderById = orders.ToDictionary(o => o.Id);
        var byModel = stepRuns
            .GroupBy(r =>
            {
                var orderId = r.StepExec?.PhaseExec?.BuildExecution?.BuildOrderId ?? 0;
                if (orderId > 0 && orderById.TryGetValue(orderId, out var ord))
                    return machineModels.GetValueOrDefault(ord.MachineModelId, "Unknown");
                return "Unknown";
            })
            .OrderByDescending(g => g.Count())
            .Take(10);
        sb.AppendLine("Machine model activity (top 10 by step count):");
        foreach (var g in byModel)
            sb.AppendLine($"  {g.Key}: {g.Count()} steps");
        sb.AppendLine();

        if (interruptions.Any())
        {
            sb.AppendLine($"Production interruptions: {interruptions.Count}");
            var irTypes = interruptions.GroupBy(i => i.Category.ToString()).OrderByDescending(g => g.Count());
            foreach (var g in irTypes)
                sb.AppendLine($"  {g.Key}: {g.Count()}");
            sb.AppendLine();
        }

        var blocked = orders.Where(o => o.IsBlocked).ToList();
        if (blocked.Any())
        {
            sb.AppendLine("Blocked orders detail:");
            foreach (var o in blocked.Take(20))
                sb.AppendLine($"  Order {o.ExternalRef} / {o.SerialNumber}: {o.BlockedReason?.Name ?? "No reason"} — {o.BlockedNotes ?? ""}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public async Task<IActionResult> OnPostGenerateAsync()
    {
        // ensure TechnicianId reflects the user selected in the filters (UserIdFilter)
        TechnicianId = UserIdFilter;
        await PopulateDropdownsAsync();
        await LoadPromptsAsync();

        var from = FromDate ?? DateTime.UtcNow.Date.AddDays(-30);
        var to = (ToDate ?? DateTime.UtcNow.Date).AddDays(1);

        var dataText = await BuildDataTextAsync(from, to);

        string promptTemplate = EditedPromptText;
        if (string.IsNullOrWhiteSpace(promptTemplate))
        {
            var promptRecord = await _db.AiPrompts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == SelectedPromptId);
            if (promptRecord == null)
            {
                ErrorMessage = "Prompt not found.";
                return Page();
            }
            promptTemplate = promptRecord.PromptText;
        }

        var finalPrompt = promptTemplate.Replace("{DATA}", dataText);
        LastPrompt = finalPrompt;

        var (result, error) = await _gpt.GenerateAsync(finalPrompt);
        if (error != null)
        {
            ErrorMessage = error;
            Generated = false;
        }
        else
        {
            ReportText = result;
            Generated = true;
            TempData["Success"] = "Report generated.";
        }

        await LoadActivityAsync(); // refresh rows if needed
        return Page();
    }

    private async Task PopulateDropdownsAsync()
    {
        var technicianIds = await _db.StepRuns
            .Where(r => !string.IsNullOrEmpty(r.UserId))
            .Select(r => r.UserId)
            .Distinct()
            .ToListAsync();

        var techItems = new List<SelectListItem> { new("— All technicians —", "") };
        foreach (var uid in technicianIds.OrderBy(x => x))
        {
            var u = await _userManager.FindByIdAsync(uid);
            var name = u?.UserName ?? u?.Email ?? uid;
            var at = name.IndexOf('@');
            techItems.Add(new SelectListItem(at > 0 ? name[..at] : name, uid));
        }
        TechnicianList = new SelectList(techItems, "Value", "Text", TechnicianId);

        var models = await _db.MachineModels.AsNoTracking()
            .OrderBy(m => m.Name)
            .Select(m => new { m.Id, m.Name })
            .ToListAsync();
        var modelItems = new List<SelectListItem> { new("— All machines —", "") };
        modelItems.AddRange(models.Select(m => new SelectListItem(m.Name, m.Id.ToString())));
        MachineModelList = new SelectList(modelItems, "Value", "Text", MachineModelId?.ToString());
    }

    private async Task LoadPromptsAsync()
    {
        var prompts = await _db.Set<AiPrompt>().AsNoTracking().OrderBy(p => p.Name).ToListAsync();
        PromptSelectList = new SelectList(prompts, "Id", "Name", SelectedPromptId == 0 && prompts.Any() ? prompts.First().Id : SelectedPromptId);
        PromptsData = prompts.ToDictionary(p => p.Id, p => p.PromptText);
        if (SelectedPromptId == 0 && prompts.Any()) SelectedPromptId = prompts.First().Id;
        EditedPromptText = PromptsData.GetValueOrDefault(SelectedPromptId, "");
    }

    private async Task LoadActivityAsync()
    {
        var fromUtc = DateTime.SpecifyKind(FromDate?.Date ?? DateTime.UtcNow.Date, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind((ToDate?.Date ?? DateTime.UtcNow.Date).AddDays(1), DateTimeKind.Utc);

        var query = _db.StepRuns
            .AsNoTracking()
            .Include(r => r.StepExec)
                .ThenInclude(se => se.PhaseExec)
                    .ThenInclude(pe => pe.BuildExecution)
                        .ThenInclude(be => be.BuildOrder)
            .Where(r => r.StartedAt >= fromUtc && r.StartedAt < toUtc);

        if (!string.IsNullOrWhiteSpace(UserIdFilter))
            query = query.Where(r => r.UserId == UserIdFilter);

        var runs = await query.OrderBy(r => r.StartedAt).ToListAsync();

        var orderIds = runs.Select(r => r.StepExec!.PhaseExec!.BuildExecution!.BuildOrderId).Distinct().ToList();
        var orders = await _db.BuildOrders.Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o);
        var modelIds = orders.Values.Select(o => o.MachineModelId).Distinct().ToList();
        var variantIds = orders.Values.Select(o => o.MachineVariantId).Distinct().ToList();
        var models = await _db.MachineModels.Where(m => modelIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Name);
        var variants = await _db.MachineVariants.Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Name);

        var userIds = runs.Select(r => r.UserId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var users = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => (u.UserName ?? u.Email ?? u.Id));

        var utcNow = DateTime.UtcNow;

        Rows = runs.Select(r =>
        {
            var order = orders.GetValueOrDefault(r.StepExec!.PhaseExec!.BuildExecution!.BuildOrderId);
            var duration = r.FinishedAt.HasValue ? r.DurationSeconds : (int?)(utcNow - r.StartedAt).TotalSeconds;
            return new ActivityRow
            {
                StepRunId = r.Id,
                StartedAtUtc = r.StartedAt,
                StartedAtLocal = r.StartedAt.ToLocalTime(),
                UserId = r.UserId ?? "",
                UserDisplayName = users.TryGetValue(r.UserId ?? "", out var dn) ? dn : r.UserId ?? "",
                MachineModel = order != null && models.TryGetValue(order.MachineModelId, out var mn) ? mn : "",
                MachineVariant = order != null && variants.TryGetValue(order.MachineVariantId, out var vn) ? vn : "",
                OrderRef = order?.ExternalRef ?? "",
                SerialNumber = order?.SerialNumber ?? "",
                StepTitle = r.StepExec?.Title ?? "",
                DurationSeconds = duration,
                InProgress = !r.FinishedAt.HasValue
            };
        }).ToList();
    }

    public async Task<IActionResult> OnPostSavePromptAsync()
    {
        var p = await _db.Set<AiPrompt>().FindAsync(SelectedPromptId);
        if (p == null) return NotFound();
        p.PromptText = EditedPromptText ?? "";
        p.LastUpdate = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Prompt updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetDownloadDataAsync(string format = "csv")
    {
        await LoadActivityAsync();
        if (format == "json")
        {
            var json = JsonSerializer.Serialize(Rows);
            var name = $"Activity_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            return File(Encoding.UTF8.GetBytes(json), "application/json", name);
        }

        var sb = new StringBuilder();
        sb.AppendLine("User,UserDisplay,StartedUtc,StartedLocal,OrderRef,Serial,Model,Variant,Step,DurationSeconds,InProgress");
        foreach (var r in Rows)
        {
            string CsvEscape(string v)
            {
                if (v == null) v = "";
                if (v.Contains(',') || v.Contains('"') || v.Contains('\n')) return '"' + v.Replace("\"", "\"\"") + '"';
                return v;
            }
            sb.AppendLine(string.Join(",", new[] {
                CsvEscape(r.UserId), CsvEscape(r.UserDisplayName), r.StartedAtUtc.ToString("o"), r.StartedAtLocal.ToString("o"), CsvEscape(r.OrderRef), CsvEscape(r.SerialNumber), CsvEscape(r.MachineModel), CsvEscape(r.MachineVariant), CsvEscape(r.StepTitle), r.DurationSeconds?.ToString() ?? "", r.InProgress.ToString()
            }));
        }
        var fileName = $"Activity_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
    }
}
