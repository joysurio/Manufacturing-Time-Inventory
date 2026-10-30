using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Execution;

namespace ManufacturingTimeTracking.Pages.Reports;

[Authorize(Roles = "Admin,Supervisor")]
public class KpisSupervisorModel : PageModel
{
    private readonly ApplicationDbContext _db;
    public KpisSupervisorModel(ApplicationDbContext db) { _db = db; }

    [BindProperty(SupportsGet = true)] public DateTime? FromDate { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? ToDate { get; set; }
    [BindProperty(SupportsGet = true)] public int? SelectedModelId { get; set; }
    public IEnumerable<SelectListItem>? ModelSelectList { get; set; }

    // Data exposed to view
    public object OverTargetSummary { get; set; } = new { labels = new string[0], values = new int[0] };
    public int[] OverTargetOrderIds { get; set; } = new int[0];
    public object ReworkSummary { get; set; } = new { labels = new string[0], values = new int[0] };
    public object BlockSummary { get; set; } = new { labels = new string[0], values = new int[0] };
    // Weekly machines exceeding theoretical (labels = week, values = count)
    public object WeeklyExceededSummary { get; set; } = new { labels = new string[0], values = new int[0] };
    public object WeeklyExceededDetails { get; set; } = new object();
    // diagnostics for weekly exceeded calculation
    public int WeeklyExecutionsScanned { get; set; }
    public int WeeklyExecutionsWithTheoretical { get; set; }
    public int WeeklyExecutionsExceeded { get; set; }
    public List<object> WeeklySampleExceeded { get; set; } = new();
    // Weekly reworks (count per week)
    public object WeeklyReworksSummary { get; set; } = new { labels = new string[0], values = new int[0] };
    public object WeeklyReworksDetails { get; set; } = new object();
    public int WeeklyReworksTotal { get; set; }
    // Weekly open orders (count per week)
    public object WeeklyOpenOrdersSummary { get; set; } = new { labels = new string[0], values = new int[0] };
    public object WeeklyOpenOrdersDetails { get; set; } = new object();
    public int WeeklyOpenOrdersTotal { get; set; }
    // detailed list of over-target steps per order (ordered to match OverTargetSummary.labels)
    public object OverTargetDetails { get; set; } = new object();
    // Diagnostic counts
    public int RunsCheckedCount { get; set; }
    public int RunsWithTargetCount { get; set; }
    public int ReworksCount { get; set; }
    public int BlocksCount { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var models = await _db.MachineModels.AsNoTracking().OrderBy(m => m.Name).ToListAsync();
        // Build select list with an initial "Todos" option (empty value -> binds to null for int?)
        var modelItems = new List<SelectListItem>();
        modelItems.Add(new SelectListItem { Value = "", Text = "Todos", Selected = !SelectedModelId.HasValue });
        modelItems.AddRange(models.Select(m => new SelectListItem { Value = m.Id.ToString(), Text = m.Name, Selected = SelectedModelId.HasValue && SelectedModelId.Value == m.Id }));
        ModelSelectList = modelItems;

        // Default date range: Monday of current week to today
        var today = DateTime.UtcNow.Date;
        var daysFromMonday = ((int)today.DayOfWeek + 6) % 7; // Monday=0, Sunday=6
        var monday = today.AddDays(-daysFromMonday);

        if (!FromDate.HasValue) FromDate = monday;
        if (!ToDate.HasValue) ToDate = today;

        var from = FromDate?.Date;
        var to = ToDate?.Date.AddDays(1);

        // Base runs query: completed runs
        var runsQuery = _db.StepRuns.AsNoTracking().Include(r => r.StepExec).ThenInclude(se => se.PhaseExec).ThenInclude(pe => pe.BuildExecution).Where(r => r.FinishedAt != null && r.DurationSeconds != null);
        if (from.HasValue) runsQuery = runsQuery.Where(r => r.StartedAt >= DateTime.SpecifyKind(from.Value, DateTimeKind.Utc));
        if (to.HasValue) runsQuery = runsQuery.Where(r => r.StartedAt < DateTime.SpecifyKind(to.Value, DateTimeKind.Utc));
        if (SelectedModelId.HasValue)
        {
            runsQuery = runsQuery.Where(r => r.StepExec != null && r.StepExec.PhaseExec != null && r.StepExec.PhaseExec.BuildExecution != null && r.StepExec.PhaseExec.BuildExecution.BuildOrder != null && r.StepExec.PhaseExec.BuildExecution.BuildOrder.MachineModelId == SelectedModelId.Value);
        }

        var runs = await runsQuery.Select(r => new {
            r.DurationSeconds,
            TemplateId = r.StepExec.StepTemplateId,
            StepExecTarget = r.StepExec.TargetSeconds,
            StepExecTitle = r.StepExec.Title,
            OrderId = r.StepExec.PhaseExec.BuildExecution.BuildOrderId,
            ModelId = r.StepExec.PhaseExec.BuildExecution.BuildOrder.MachineModelId,
            VariantId = r.StepExec.PhaseExec.BuildExecution.BuildOrder.MachineVariantId,
            OperatorUserId = r.UserId
        }).ToListAsync();

        // resolve operator display names
        var userIds = runs.Select(x => x.OperatorUserId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        var userMap = new Dictionary<string, string>();
        if (userIds.Any())
        {
            var users = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToListAsync();
            foreach (var u in users)
            {
                var name = (!string.IsNullOrWhiteSpace(u.FirstName) || !string.IsNullOrWhiteSpace(u.LastName))
                    ? $"{(u.FirstName ?? "").Trim()} {(u.LastName ?? "").Trim()}".Trim()
                    : (u.Email ?? u.UserName ?? u.Id);
                var at = name.IndexOf('@'); if (at > 0) name = name[..at];
                userMap[u.Id] = name;
            }
        }

        var tplIds = runs.Select(r => r.TemplateId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var tplMap = new Dictionary<int,int?>();
        if (tplIds.Any()) tplMap = await _db.StepTemplates.AsNoTracking().Where(t => tplIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.TargetSeconds);

        // preload templates by model/variant/title for fallback matching
        var modelIds = runs.Select(r => r.ModelId).Distinct().ToList();
        var variantIds = runs.Select(r => r.VariantId).Distinct().ToList();
        var templatesByKey = new Dictionary<(int modelId,int variantId,string title), int?>();
        if (modelIds.Any() && variantIds.Any())
        {
            var templates = await _db.StepTemplates.AsNoTracking()
                .Include(st => st.PhaseTemplate).ThenInclude(pt => pt.ProcessTemplate)
                .Where(st => st.PhaseTemplate != null && st.PhaseTemplate.ProcessTemplate != null && modelIds.Contains(st.PhaseTemplate.ProcessTemplate.MachineModelId) && variantIds.Contains(st.PhaseTemplate.ProcessTemplate.MachineVariantId))
                .ToListAsync();
            foreach (var t in templates)
            {
                var key = (t.PhaseTemplate.ProcessTemplate.MachineModelId, t.PhaseTemplate.ProcessTemplate.MachineVariantId, (t.Title ?? "").Trim());
                if (!templatesByKey.ContainsKey(key)) templatesByKey[key] = t.TargetSeconds;
            }
        }

        var overCounts = new Dictionary<string,int>();
        var overDetails = new Dictionary<string, List<object>>();
        foreach (var r in runs)
        {
            int? target = null;
            // prefer stepExec snapshot if available
            if (r.StepExecTarget.HasValue) target = r.StepExecTarget;
            // fallback to template by id
            if (target == null && r.TemplateId.HasValue && tplMap.TryGetValue(r.TemplateId.Value, out var t)) target = t;
            // fallback to template by title/model/variant
            if (target == null)
            {
                var key = (r.ModelId, r.VariantId, (r.StepExecTitle ?? "").Trim());
                if (!string.IsNullOrEmpty(key.Item3) && templatesByKey.TryGetValue(key, out var tt)) target = tt;
            }

            if (target.HasValue && r.DurationSeconds.HasValue && r.DurationSeconds.Value > target.Value)
            {
                var key = r.OrderId.ToString();
                overCounts[key] = overCounts.ContainsKey(key) ? overCounts[key] + 1 : 1;
                // add detail record
                var duration = r.DurationSeconds.Value;
                var op = string.IsNullOrWhiteSpace(r.OperatorUserId) ? "" : (userMap.ContainsKey(r.OperatorUserId) ? userMap[r.OperatorUserId] : r.OperatorUserId);
                var detail = new { StepTitle = r.StepExecTitle ?? "(step)", DurationSeconds = duration, Duration = TimeSpan.FromSeconds(duration).ToString(@"hh\:mm\:ss"), TargetSeconds = target.Value, Target = TimeSpan.FromSeconds(target.Value).ToString(@"hh\:mm\:ss"), Operator = op };
                if (!overDetails.ContainsKey(key)) overDetails[key] = new List<object>();
                overDetails[key].Add(detail);
            }
        }
        // diagnostics
        RunsCheckedCount = runs.Count;
        RunsWithTargetCount = runs.Count(r => {
            int? target = null;
            if (r.StepExecTarget.HasValue) target = r.StepExecTarget;
            if (target == null && r.TemplateId.HasValue && tplMap.TryGetValue(r.TemplateId.Value, out var t)) target = t;
            if (target == null)
            {
                var key = (r.ModelId, r.VariantId, (r.StepExecTitle ?? "").Trim());
                if (!string.IsNullOrEmpty(key.Item3) && templatesByKey.TryGetValue(key, out var tt)) target = tt;
            }
            return target.HasValue;
        });
        var orderKeys = overCounts.Keys.Select(k => { int.TryParse(k, out var v); return v; }).Where(id => id > 0).ToList();
        var orderLabels = new List<string>();
        if (orderKeys.Any())
        {
            var orders = await _db.BuildOrders.AsNoTracking().Where(o => orderKeys.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => new { o.ExternalRef, o.SerialNumber });
            foreach (var k in overCounts.Keys)
            {
                if (int.TryParse(k, out var oid) && orders.TryGetValue(oid, out var od))
                {
                    var label = string.IsNullOrWhiteSpace(od.ExternalRef) ? (string.IsNullOrWhiteSpace(od.SerialNumber) ? $"Order {oid}" : od.SerialNumber) : od.ExternalRef;
                    // include serial if external ref is short
                    if (!string.IsNullOrWhiteSpace(od.ExternalRef) && !string.IsNullOrWhiteSpace(od.SerialNumber))
                        label = $"{od.ExternalRef} / {od.SerialNumber}";
                    orderLabels.Add(label);
                }
                else
                {
                    orderLabels.Add(k);
                }
            }
        }
        else
        {
            orderLabels.AddRange(overCounts.Keys);
        }

        OverTargetSummary = new { labels = orderLabels.ToArray(), values = overCounts.Values.ToArray() };
        // Build ordered details list matching labels
        var orderedKeys = overCounts.Keys.ToList();
        var orderedDetails = new List<object>();
        var orderedIds = new List<int>();
        foreach (var k in orderedKeys)
        {
            if (overDetails.TryGetValue(k, out var list)) orderedDetails.Add(list);
            else orderedDetails.Add(new List<object>());
            if (int.TryParse(k, out var pid)) orderedIds.Add(pid);
            else orderedIds.Add(0);
        }
        OverTargetDetails = orderedDetails;
        OverTargetOrderIds = orderedIds.ToArray();

        var reworksQuery = _db.ProductionInterruptions.AsNoTracking().Where(pi => pi.Category == ProductionInterruptionCategory.Reopened);
        if (from.HasValue) reworksQuery = reworksQuery.Where(pi => pi.StartTime >= DateTime.SpecifyKind(from.Value, DateTimeKind.Utc));
        if (to.HasValue) reworksQuery = reworksQuery.Where(pi => pi.StartTime < DateTime.SpecifyKind(to.Value, DateTimeKind.Utc));
        if (SelectedModelId.HasValue) reworksQuery = reworksQuery.Where(pi => pi.BuildOrder != null && pi.BuildOrder.MachineModelId == SelectedModelId.Value);
        var reworks = await reworksQuery.ToListAsync();
        var reworkGroups = reworks.GroupBy(r => string.IsNullOrWhiteSpace(r.Notes) ? "(no reason)" : r.Notes).OrderByDescending(g => g.Count()).Take(10).ToDictionary(g => g.Key, g => g.Count());
        ReworkSummary = new { labels = reworkGroups.Keys.ToArray(), values = reworkGroups.Values.ToArray() };

        var blocksQuery = _db.ProductionInterruptions.AsNoTracking().Where(pi => pi.Category == ProductionInterruptionCategory.Blocked);
        if (from.HasValue) blocksQuery = blocksQuery.Where(pi => pi.StartTime >= DateTime.SpecifyKind(from.Value, DateTimeKind.Utc));
        if (to.HasValue) blocksQuery = blocksQuery.Where(pi => pi.StartTime < DateTime.SpecifyKind(to.Value, DateTimeKind.Utc));
        if (SelectedModelId.HasValue) blocksQuery = blocksQuery.Where(pi => pi.BuildOrder != null && pi.BuildOrder.MachineModelId == SelectedModelId.Value);
        var blocks = await blocksQuery.ToListAsync();
        var blockGroups = blocks.GroupBy(b => string.IsNullOrWhiteSpace(b.Notes) ? "(no reason)" : b.Notes).OrderByDescending(g => g.Count()).Take(10).ToDictionary(g => g.Key, g => g.Count());
        BlockSummary = new { labels = blockGroups.Keys.ToArray(), values = blockGroups.Values.ToArray() };

        // Weekly machines exceeding theoretical - independent of page filters (use last N weeks)
        const int weeks = 12;
        // Align weeks to Monday: find the Monday of current week, then go back 12 weeks
        var todayUtcWeekly = DateTime.UtcNow.Date;
        var daysFromMondayWeekly = ((int)todayUtcWeekly.DayOfWeek + 6) % 7; // Monday=0, Sunday=6
        var thisMonday = todayUtcWeekly.AddDays(-daysFromMondayWeekly);
        var weekStart = thisMonday.AddDays(-7 * (weeks - 1)); // start of first week (Monday)
        var weekEnd = thisMonday.AddDays(7); // exclusive end (next Monday)

        // broaden selection: include executions that are completed or have any finished runs
        var executions = await _db.BuildExecutions.AsNoTracking()
            .Include(be => be.BuildOrder)
            .Include(be => be.Phases).ThenInclude(p => p.Steps)
                .ThenInclude(s => s.Runs)
            .Where(be => be.FinishedAt != null || be.Status == "Completed" || be.Phases.Any(p => p.Steps.Any(s => s.Runs.Any(r => r.FinishedAt != null))))
            .ToListAsync();

        // collect template ids referenced by steps where target missing
        var stepTemplateIds = executions.SelectMany(be => be.Phases.SelectMany(p => p.Steps).Select(s => s.StepTemplateId)).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var tplMapAll = new Dictionary<int, int?>();
        if (stepTemplateIds.Any()) tplMapAll = await _db.StepTemplates.AsNoTracking().Where(t => stepTemplateIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.TargetSeconds);

        // also preload templates by model/variant/title for fallback matching (use current template targets)
        // derive model/variant from execution build order
        var modelIdsExec = executions.Select(be => be.BuildOrder?.MachineModelId ?? 0).Where(id => id > 0).Distinct().ToList();
        var variantIdsExec = executions.Select(be => be.BuildOrder?.MachineVariantId ?? 0).Where(id => id > 0).Distinct().ToList();
        var templatesByKeyAll = new Dictionary<(int modelId,int variantId,string title), int?>();

        // Normalizes titles: trim, to lower, remove diacritics for robust matching
        static string NormalizeTitle(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var normalized = s.Trim().ToLowerInvariant();
            var formD = normalized.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (var ch in formD)
            {
                var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
                if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        if (modelIdsExec.Any() && variantIdsExec.Any())
        {
            var templatesAll = await _db.StepTemplates.AsNoTracking()
                .Include(st => st.PhaseTemplate).ThenInclude(pt => pt.ProcessTemplate)
                .Where(st => st.PhaseTemplate != null && st.PhaseTemplate.ProcessTemplate != null && modelIdsExec.Contains(st.PhaseTemplate.ProcessTemplate.MachineModelId) && variantIdsExec.Contains(st.PhaseTemplate.ProcessTemplate.MachineVariantId))
                .ToListAsync();
            foreach (var t in templatesAll)
            {
                var key = (t.PhaseTemplate.ProcessTemplate.MachineModelId, t.PhaseTemplate.ProcessTemplate.MachineVariantId, NormalizeTitle(t.Title ?? ""));
                if (!templatesByKeyAll.ContainsKey(key)) templatesByKeyAll[key] = t.TargetSeconds;
            }
        }

        // prepare weekly buckets
        var weekLabels = new List<string>();
        var weekCounts = new List<int>();
        var weekDetails = new List<List<object>>();
        for (int i = 0; i < weeks; i++)
        {
            var s = weekStart.AddDays(7 * i);
            var e = s.AddDays(7);
            weekLabels.Add(s.ToString("yyyy-MM-dd"));
            weekCounts.Add(0);
            weekDetails.Add(new List<object>());
        }

        // resolve user names for runs inside these executions
        var execUserIds = executions.SelectMany(be => be.Phases.SelectMany(p => p.Steps).SelectMany(s => s.Runs).Select(r => r.UserId)).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        var execUserMap = new Dictionary<string, string>();
        if (execUserIds.Any())
        {
            var execUsers = await _db.Users.AsNoTracking().Where(u => execUserIds.Contains(u.Id)).ToListAsync();
            foreach (var u in execUsers)
            {
                var name = (!string.IsNullOrWhiteSpace(u.FirstName) || !string.IsNullOrWhiteSpace(u.LastName))
                    ? $"{(u.FirstName ?? "").Trim()} {(u.LastName ?? "").Trim()}".Trim()
                    : (u.Email ?? u.UserName ?? u.Id);
                var at = name.IndexOf('@'); if (at > 0) name = name[..at];
                execUserMap[u.Id] = name;
            }
        }

        foreach (var be in executions)
        {
            // determine a finish time: prefer BuildExecution.FinishedAt, else latest run finishedAt, else skip
            DateTime? finished = null;
            if (be.FinishedAt.HasValue) finished = be.FinishedAt.Value.ToUniversalTime();
            else
            {
                var maxRun = be.Phases.SelectMany(p => p.Steps).SelectMany(s => s.Runs).Where(r => r.FinishedAt != null).OrderByDescending(r => r.FinishedAt).FirstOrDefault();
                if (maxRun != null) finished = maxRun.FinishedAt.Value.ToUniversalTime();
            }
            if (!finished.HasValue) continue;
            // find bucket index
            var diffDays = (finished.Value.Date - weekStart).TotalDays;
            if (diffDays < 0) continue;
            var idx = (int)(diffDays / 7);
            if (idx < 0 || idx >= weeks) continue;

            // compute theoretical and actual
            int theoretical = 0;
            int actual = 0;
            foreach (var phase in be.Phases)
            {
                foreach (var step in phase.Steps)
                {
                    // Determine theoretical target: prefer current template target (by id), else match template by model/variant/title, else fallback to per-execution snapshot
                    int? t = null;
                    if (step.StepTemplateId.HasValue && tplMapAll.TryGetValue(step.StepTemplateId.Value, out var tv)) t = tv;
                    if (t == null)
                    {
                        // try match by model/variant/title using execution's order
                        try
                        {
                            var order = be.BuildOrder;
                            var modelId = order?.MachineModelId ?? 0;
                            var variantId = order?.MachineVariantId ?? 0;
                            var titleNorm = NormalizeTitle(step.Title ?? "");
                            if (modelId > 0 && variantId > 0 && !string.IsNullOrEmpty(titleNorm))
                            {
                                var key = (modelId, variantId, titleNorm);
                                if (templatesByKeyAll.TryGetValue(key, out var ttv)) t = ttv;
                            }
                        }
                        catch { }
                    }
                    // fallback to per-execution snapshot only if template has no target
                    if (t == null && step.TargetSeconds.HasValue) t = step.TargetSeconds;
                    if (t.HasValue) theoretical += t.Value;
                    var stepTotal = step.Runs.Where(r => r.DurationSeconds.HasValue).Sum(r => r.DurationSeconds!.Value);
                    actual += stepTotal;
                }
            }

            // diagnostics counts
            WeeklyExecutionsScanned++;
            if (theoretical > 0) WeeklyExecutionsWithTheoretical++;

            if (theoretical > 0 && actual > theoretical)
            {
                // build step-level details for this execution: include only steps where actual > theoretical
                var stepDetails = new List<object>();
                foreach (var phase in be.Phases)
                {
                    foreach (var step in phase.Steps)
                    {
                        // compute step theoretical and actual again (same as above)
                        int? st = null;
                        if (step.StepTemplateId.HasValue && tplMapAll.TryGetValue(step.StepTemplateId.Value, out var stv)) st = stv;
                        if (st == null)
                        {
                            try
                            {
                                var order = be.BuildOrder;
                                var modelId = order?.MachineModelId ?? 0;
                                var variantId = order?.MachineVariantId ?? 0;
                                var titleNorm = NormalizeTitle(step.Title ?? "");
                                if (modelId > 0 && variantId > 0 && !string.IsNullOrEmpty(titleNorm))
                                {
                                    var key = (modelId, variantId, titleNorm);
                                    if (templatesByKeyAll.TryGetValue(key, out var ttv2)) st = ttv2;
                                }
                            }
                            catch { }
                        }
                        if (st == null && step.TargetSeconds.HasValue) st = step.TargetSeconds;
                        var stepActual = step.Runs.Where(r => r.DurationSeconds.HasValue).Sum(r => r.DurationSeconds!.Value);
                        if (st.HasValue && stepActual > st.Value)
                        {
                            // collect operators for this step
                            var opIds = step.Runs.Where(r => !string.IsNullOrWhiteSpace(r.UserId)).Select(r => r.UserId).Distinct().ToList();
                            var ops = opIds.Select(id => execUserMap.ContainsKey(id) ? execUserMap[id] : id).ToArray();
                            stepDetails.Add(new { StepTitle = step.Title, Theoretical = st.Value, Actual = stepActual, Operators = ops });
                        }
                    }
                }

                weekCounts[idx]++;
                var label = !string.IsNullOrWhiteSpace(be.BuildOrder?.ExternalRef) ? be.BuildOrder.ExternalRef : (be.BuildOrder?.SerialNumber ?? ("Order " + be.BuildOrderId));
                var entry = new { OrderId = be.BuildOrderId, Label = label, Theoretical = theoretical, Actual = actual, Steps = stepDetails };
                weekDetails[idx].Add(entry);
                WeeklyExecutionsExceeded++;
            }
        }

        WeeklyExceededSummary = new { labels = weekLabels.ToArray(), values = weekCounts.ToArray() };
        WeeklyExceededDetails = weekDetails.Select(w => w.ToArray()).ToArray();

        // Weekly reworks: count ProductionInterruptions with Category.Reopened per week (not grouped by machine)
        var reworksAll = await _db.ProductionInterruptions.AsNoTracking()
            .Include(pi => pi.BuildOrder)
            .Where(pi => pi.Category == ProductionInterruptionCategory.Reopened && pi.StartTime >= weekStart && pi.StartTime < weekEnd)
            .ToListAsync();

        var reworkWeekCounts = new int[weeks];
        var reworkWeekDetails = new List<List<object>>();
        for (int i = 0; i < weeks; i++) reworkWeekDetails.Add(new List<object>());
        foreach (var rw in reworksAll)
        {
            var dt = rw.StartTime.ToUniversalTime().Date;
            var diff = (dt - weekStart).TotalDays;
            if (diff < 0) continue;
            var idx = (int)(diff / 7);
            if (idx < 0 || idx >= weeks) continue;
            reworkWeekCounts[idx]++;
            var label = !string.IsNullOrWhiteSpace(rw.BuildOrder?.ExternalRef) ? rw.BuildOrder.ExternalRef : (rw.BuildOrder?.SerialNumber ?? ("Order " + rw.BuildOrderId));
            reworkWeekDetails[idx].Add(new { OrderId = rw.BuildOrderId, Label = label, Notes = rw.Notes, Time = rw.StartTime });
            WeeklyReworksTotal++;
        }

        WeeklyReworksSummary = new { labels = weekLabels.ToArray(), values = reworkWeekCounts };
        WeeklyReworksDetails = reworkWeekDetails.Select(w => w.ToArray()).ToArray();

        // Weekly open orders: count BuildOrders that are still open (Pending/InProgress) at each week end
        var allOrders = await _db.BuildOrders.AsNoTracking().ToListAsync();
        var openCounts = new int[weeks];
        var openDetails = new List<List<object>>();
        for (int i = 0; i < weeks; i++) openDetails.Add(new List<object>());
        for (int i = 0; i < weeks; i++)
        {
            var bucketStart = weekStart.AddDays(7 * i);
            var bucketEnd = bucketStart.AddDays(7);
            var count = 0;
            foreach (var o in allOrders)
            {
                // consider orders created on or before bucketEnd
                if (o.CreatedAt.ToUniversalTime() < bucketEnd)
                {
                    // open if status is Pending or InProgress (treat Cancelled/Completed as closed)
                    if (string.Equals(o.Status, "Pending", StringComparison.OrdinalIgnoreCase) || string.Equals(o.Status, "InProgress", StringComparison.OrdinalIgnoreCase))
                    {
                        count++;
                        var label = string.IsNullOrWhiteSpace(o.ExternalRef) ? (string.IsNullOrWhiteSpace(o.SerialNumber) ? $"Order {o.Id}" : o.SerialNumber) : o.ExternalRef;
                        openDetails[i].Add(new { OrderId = o.Id, Label = label, Status = o.Status, CreatedAt = o.CreatedAt });
                        WeeklyOpenOrdersTotal++;
                    }
                }
            }
            openCounts[i] = count;
        }
        WeeklyOpenOrdersSummary = new { labels = weekLabels.ToArray(), values = openCounts };
        WeeklyOpenOrdersDetails = openDetails.Select(w => w.ToArray()).ToArray();

        return Page(); // Returning the page for the KpisSupervisor
    }
}
