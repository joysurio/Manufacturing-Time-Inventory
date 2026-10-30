using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using Microsoft.AspNetCore.Identity;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Models.Templates;
using Microsoft.AspNetCore.Mvc;

namespace ManufacturingTimeTracking.Pages.Reports;

[Authorize(Policy = "CanUseTimeManagement")]
public class ModelMachinesAnalysisModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public ModelMachinesAnalysisModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<IActionResult> OnPostToggleHiddenAsync([FromBody] ToggleHiddenRequest req)
    {
        if (req == null || req.TemplateId <= 0) return new JsonResult(new { success = false });

        var tpl = await _db.StepTemplates.FindAsync(req.TemplateId);
        if (tpl == null) return new JsonResult(new { success = false });

        tpl.Hidden = !tpl.Hidden;
        _db.Update(tpl);
        await _db.SaveChangesAsync();

        return new JsonResult(new { success = true, hidden = tpl.Hidden });
    }

    public class ToggleHiddenRequest { public int TemplateId { get; set; } }

    public class UpdateTargetRequest { public int TemplateId { get; set; } public int? TargetSeconds { get; set; } }

    [BindProperty(SupportsGet = true)]
    public int? SelectedModelId { get; set; }
    public SelectList? ModelSelectList { get; set; }
    [BindProperty(SupportsGet = true)]
    public int? SelectedVariantId { get; set; }
    public SelectList? VariantSelectList { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }
    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }
    [BindProperty(SupportsGet = true)]
    public bool ShowHidden { get; set; }
    [BindProperty(SupportsGet = true)]
    public string? TechnicianId { get; set; }

    public SelectList? TechnicianList { get; set; }

    // Selected serials (ExternalRef/Serial combination as key: OrderId|Serial)
    [BindProperty(SupportsGet = true)]
    public string[] SelectedSerialKeys { get; set; } = Array.Empty<string>();

    // Data for UI
    public List<(int OrderId, string ExternalRef, string Serial)> AvailableSerials { get; set; } = new();

    // Matrix: StepTitle -> SerialKey -> TechnicianId -> seconds
    public List<string> StepTitles { get; set; } = new();
    public List<string> ColumnSerialKeys { get; set; } = new();
    public Dictionary<string, int> SerialTotalSeconds { get; set; } = new();
    public Dictionary<string, Dictionary<string, Dictionary<string, int>>> Matrix { get; set; } = new();
    // Matrix[stepTitle][serialKey][techId] = seconds

    // Expose user display names to the view
    public Dictionary<string, string> UserDisplayNames { get; set; } = new();

    // Per-step statistics (mean, median, min, max in seconds)
    public class StepStats
    {
        public int ValidCount { get; set; }
        public double MeanSeconds { get; set; }
        public double MedianSeconds { get; set; }
        public int? MinSeconds { get; set; }
        public int? MaxSeconds { get; set; }
    }

    public async Task<IActionResult> OnPostUpdateTargetAsync([FromBody] UpdateTargetRequest req)
    {
        if (req == null || req.TemplateId <= 0) return new JsonResult(new { success = false });

        var tpl = await _db.StepTemplates.FindAsync(req.TemplateId);
        if (tpl == null) return new JsonResult(new { success = false });

        tpl.TargetSeconds = req.TargetSeconds;
        _db.Update(tpl);
        await _db.SaveChangesAsync();

        return new JsonResult(new { success = true, targetSeconds = tpl.TargetSeconds });
    }

    public Dictionary<string, StepStats> StepStatsMap { get; set; } = new();

    // Map step title -> template hidden state (nullable if unknown)
    public Dictionary<string, bool?> StepTitleHidden { get; set; } = new();

    // Map step title -> template target seconds (nullable)
    public Dictionary<string, int?> StepTitleTargetSeconds { get; set; } = new();

    // Sum of all target seconds for displayed steps
    public int TotalTargetSeconds { get; set; }

    // Map step title -> StepTemplateId (nullable). Used to generate Edit links from reports.
    public Dictionary<string, int?> StepTitleToTemplateId { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        // Load machine models
        var models = await _db.MachineModels.AsNoTracking().OrderBy(m => m.Name).ToListAsync();
        if (!models.Any())
            return Page();
        ModelSelectList = new SelectList(models, "Id", "Name", SelectedModelId == null ? models.First().Id : SelectedModelId);
        if (!SelectedModelId.HasValue)
            SelectedModelId = models.First().Id;

        // Load variants for selected model
        var variants = await _db.MachineVariants.AsNoTracking()
            .Where(v => v.MachineModelId == SelectedModelId.Value)
            .OrderBy(v => v.Name)
            .ToListAsync();
        // Add an "All" option by leaving SelectedVariantId null; client can choose a specific variant
        VariantSelectList = new SelectList(variants, "Id", "Name", SelectedVariantId);

        // Build technician list for this model
        var techIds = await _db.StepRuns
            .AsNoTracking()
            .Include(r => r.StepExec).ThenInclude(se => se.PhaseExec).ThenInclude(pe => pe.BuildExecution).ThenInclude(be => be.BuildOrder)
            .Where(r => r.StepExec != null && r.StepExec.PhaseExec != null && r.StepExec.PhaseExec.BuildExecution != null && r.StepExec.PhaseExec.BuildExecution.BuildOrder != null && r.StepExec.PhaseExec.BuildExecution.BuildOrder.MachineModelId == SelectedModelId.Value)
            .Select(r => r.UserId)
            .Distinct()
            .ToListAsync();

        var techItems = new List<SelectListItem> { new("— All —", "") };
        foreach (var uid in techIds.OrderBy(x => x))
        {
            if (string.IsNullOrEmpty(uid)) continue;
            var u = await _userManager.FindByIdAsync(uid);
            var display = u?.FirstName?.Trim() + " " + u?.LastName?.Trim();
            if (string.IsNullOrWhiteSpace(display)) display = u?.UserName ?? u?.Email ?? uid;
            var at = display.IndexOf('@');
            if (at > 0) display = display.Substring(0, at);
            techItems.Add(new SelectListItem(display, uid));
        }
        TechnicianList = new SelectList(techItems, "Value", "Text", TechnicianId);

        // Default date range: all (no restriction) — user may set filters

        // Find orders for this model that have runs
        var runsQuery = _db.StepRuns
            .AsNoTracking()
            .Include(r => r.StepExec).ThenInclude(se => se.PhaseExec).ThenInclude(pe => pe.BuildExecution).ThenInclude(be => be.BuildOrder)
            .Where(r => r.StepExec != null && r.StepExec.PhaseExec != null && r.StepExec.PhaseExec.BuildExecution != null && r.StepExec.PhaseExec.BuildExecution.BuildOrder != null)
            .Where(r => r.StepExec.PhaseExec.BuildExecution.BuildOrder.MachineModelId == SelectedModelId.Value);

        if (FromDate.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(FromDate.Value.Date, DateTimeKind.Utc);
            runsQuery = runsQuery.Where(r => r.StartedAt >= fromUtc);
        }
        if (ToDate.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1), DateTimeKind.Utc);
            runsQuery = runsQuery.Where(r => r.StartedAt < toUtc);
        }
        if (!string.IsNullOrWhiteSpace(TechnicianId))
            runsQuery = runsQuery.Where(r => r.UserId == TechnicianId);
        if (SelectedVariantId.HasValue)
            runsQuery = runsQuery.Where(r => r.StepExec != null && r.StepExec.PhaseExec != null && r.StepExec.PhaseExec.BuildExecution != null && r.StepExec.PhaseExec.BuildExecution.BuildOrder != null && r.StepExec.PhaseExec.BuildExecution.BuildOrder.MachineVariantId == SelectedVariantId.Value);

        var runs = await runsQuery.OrderBy(r => r.StartedAt).ToListAsync();

        // Build available serials list
        // Ensure unique orders by Id (runs may reference same order instance multiple times)
        var orders = runs
            .Select(r => r.StepExec!.PhaseExec!.BuildExecution!.BuildOrder)
            .Where(o => o != null)
            .GroupBy(o => o.Id)
            .Select(g => g.First())
            .ToList();
        AvailableSerials = orders.Select(o => (OrderId: o.Id, ExternalRef: o.ExternalRef ?? "", Serial: o.SerialNumber ?? "")).ToList();

        // If no selected serials, default select all
        if (SelectedSerialKeys == null || SelectedSerialKeys.Length == 0)
        {
            SelectedSerialKeys = AvailableSerials.Select(a => KeyFor(a.OrderId, a.Serial)).ToArray();
        }

        ColumnSerialKeys = SelectedSerialKeys.ToList();

        // Preload templates for the selected model/variant and order by phase/step
        var templatesQuery = _db.StepTemplates
            .AsNoTracking()
            .Include(st => st.PhaseTemplate).ThenInclude(pt => pt.ProcessTemplate)
            .Where(st => st.PhaseTemplate != null && st.PhaseTemplate.ProcessTemplate != null && st.PhaseTemplate.ProcessTemplate.MachineModelId == SelectedModelId);
        if (SelectedVariantId.HasValue)
        {
            templatesQuery = templatesQuery.Where(st => st.PhaseTemplate.ProcessTemplate.MachineVariantId == SelectedVariantId.Value);
        }
        var templatesForModel = await templatesQuery
            .OrderBy(st => st.PhaseTemplate.SortOrder)
            .ThenBy(st => st.SortOrder)
            .ToListAsync();

        var templateTitlesOrdered = templatesForModel
            .Where(t => !string.IsNullOrWhiteSpace(t.Title))
            .Select(t => t.Title!)
            .Distinct()
            .ToList();

        // Build step titles from runs preserving execution order
        var orderedRunSteps = runs
            .Select(r => r.StepExec)
            .Where(se => se != null)
            .OrderBy(se => se!.PhaseExec!.SortOrder)
            .ThenBy(se => se!.SortOrder)
            .Select(se => se!.Title ?? "")
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .ToList();

        // Combine: use template order first, then append any run-only steps preserving execution order
        StepTitles = new List<string>(templateTitlesOrdered);
        foreach (var rs in orderedRunSteps)
        {
            if (!StepTitles.Contains(rs)) StepTitles.Add(rs);
        }

        // Prepare matrix
        Matrix = new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();
        SerialTotalSeconds = new Dictionary<string, int>();
        foreach (var sk in ColumnSerialKeys)
            SerialTotalSeconds[sk] = 0;

        // Map user display names
        var userIds = runs.Select(r => r.UserId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var users = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim().Length > 0 ? (u.FirstName + " " + u.LastName).Trim() : (u.UserName ?? u.Email ?? u.Id));
        foreach (var uid in users.Keys.ToList())
        {
            var nm = users[uid];
            var at = nm.IndexOf('@');
            if (at > 0) nm = nm.Substring(0, at);
            UserDisplayNames[uid] = nm;
        }

        // Initialize matrix rows
        foreach (var step in StepTitles)
        {
            Matrix[step] = new Dictionary<string, Dictionary<string, int>>();
            foreach (var sk in ColumnSerialKeys)
                Matrix[step][sk] = new Dictionary<string, int>();
        }

        // Build mapping from step title to template id using available runs (first match) and templates preloaded
        var templatesById = templatesForModel.ToDictionary(t => t.Id, t => t);
        foreach (var step in StepTitles)
        {
            var match = runs.Select(r => r.StepExec).FirstOrDefault(se => se != null && (se.Title ?? "") == step && se.StepTemplateId.HasValue);
            if (match?.StepTemplateId != null)
            {
                StepTitleToTemplateId[step] = match.StepTemplateId;
                if (templatesById.TryGetValue(match.StepTemplateId.Value, out var tpl))
                    StepTitleHidden[step] = tpl.Hidden;
                else
                    StepTitleHidden[step] = null;
                continue;
            }

            // Fallback: find a StepTemplate in the preloaded list that matches this title for the selected model/variant
            var dbMatch = templatesForModel.FirstOrDefault(st => st.Title == step);
            if (dbMatch != null)
            {
                StepTitleToTemplateId[step] = dbMatch.Id;
                StepTitleHidden[step] = dbMatch.Hidden;
                StepTitleTargetSeconds[step] = dbMatch.TargetSeconds;
            }
            else
            {
                StepTitleToTemplateId[step] = null;
                StepTitleHidden[step] = null;
                StepTitleTargetSeconds[step] = null;
            }
        }

        // If ShowHidden is false, remove hidden steps from StepTitles
        if (!ShowHidden)
        {
            StepTitles = StepTitles.Where(step => !(StepTitleHidden.TryGetValue(step, out var hv) && hv == true)).ToList();
        }

        // Compute total target seconds for displayed steps (only include steps currently in StepTitles)
        TotalTargetSeconds = StepTitles
            .Where(step => StepTitleTargetSeconds.TryGetValue(step, out var v) && v.HasValue)
            .Sum(step => StepTitleTargetSeconds[step]!.Value);

        // Fill matrix from runs
        // Create orderById safely (orders list already unique by Id)
        var orderById = orders.GroupBy(o => o.Id).ToDictionary(g => g.Key, g => g.First());
        var utcNow = DateTime.UtcNow;
        foreach (var r in runs)
        {
            var order = r.StepExec!.PhaseExec!.BuildExecution!.BuildOrder;
            var sk = KeyFor(order.Id, order.SerialNumber ?? "");
            if (!ColumnSerialKeys.Contains(sk)) continue; // skip
            var step = r.StepExec?.Title ?? "";
            var tech = r.UserId ?? "(unknown)";
            var seconds = r.FinishedAt.HasValue ? (r.DurationSeconds ?? 0) : (int)(utcNow - r.StartedAt).TotalSeconds;

            if (!Matrix.ContainsKey(step)) Matrix[step] = new Dictionary<string, Dictionary<string, int>>();
            if (!Matrix[step].ContainsKey(sk)) Matrix[step][sk] = new Dictionary<string, int>();
            if (!Matrix[step][sk].ContainsKey(tech)) Matrix[step][sk][tech] = 0;
            Matrix[step][sk][tech] += seconds;

            // SerialTotalSeconds initialized earlier for each selected serial key — just add seconds
            SerialTotalSeconds[sk] += seconds;
        }

        // After filling matrix, compute per-step stats using serial totals (exclude <5min or >6h)
        const int MinValid = 5 * 60; // 300s
        const int MaxValid = 6 * 60 * 60; // 21600s

        foreach (var step in StepTitles)
        {
            var values = new List<int>();
            foreach (var sk in ColumnSerialKeys)
            {
                var total = 0;
                if (Matrix.TryGetValue(step, out var row) && row.TryGetValue(sk, out var techs))
                {
                    total = techs.Values.Sum();
                }
                if (total >= MinValid && total <= MaxValid)
                    values.Add(total);
            }

            if (values.Count == 0)
            {
                StepStatsMap[step] = new StepStats { ValidCount = 0, MeanSeconds = 0, MedianSeconds = 0, MinSeconds = null, MaxSeconds = null };
                continue;
            }
            // Exclude zero values explicitly from statistics
            values = values.Where(v => v != 0).ToList();
            if (values.Count == 0)
            {
                StepStatsMap[step] = new StepStats { ValidCount = 0, MeanSeconds = 0, MedianSeconds = 0, MinSeconds = null, MaxSeconds = null };
                continue;
            }

            values.Sort();
            double mean = values.Average();
            double median;
            int n = values.Count;
            if (n % 2 == 1)
                median = values[n / 2];
            else
                median = (values[n / 2 - 1] + values[n / 2]) / 2.0;

            StepStatsMap[step] = new StepStats
            {
                ValidCount = values.Count,
                MeanSeconds = mean,
                MedianSeconds = median,
                MinSeconds = values.Min(),
                MaxSeconds = values.Max()
            };
        }

        return Page();
    }

    // AJAX handler: return variants for a given model as JSON
    public async Task<IActionResult> OnGetVariantsAsync(int modelId)
    {
        var variants = await _db.MachineVariants
            .AsNoTracking()
            .Where(v => v.MachineModelId == modelId)
            .OrderBy(v => v.Name)
            .Select(v => new { id = v.Id, name = v.Name })
            .ToListAsync();

        return new JsonResult(variants);
    }

    public static string KeyFor(int orderId, string serial) => $"{orderId}|{serial}";
}
