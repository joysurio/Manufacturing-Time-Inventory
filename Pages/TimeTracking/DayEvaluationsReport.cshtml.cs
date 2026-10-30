using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Models.TimeTracking;
using ManufacturingTimeTracking.Services;

namespace ManufacturingTimeTracking.Pages.TimeTracking;

/// <summary>Admin/Supervisor: review stored end-of-day evaluation (colored icons) per user and date.</summary>
[Authorize(Roles = "Admin,Supervisor")]
public class DayEvaluationsReportModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DayEvaluationsReportModel> _logger;

    public DayEvaluationsReportModel(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ILogger<DayEvaluationsReportModel> logger)
    {
        _context = context;
        _userManager = userManager;
        _configuration = configuration;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? UserIdFilter { get; set; }

    public SelectList? UserSelectList { get; set; }
    public List<EvaluationRow> Rows { get; set; } = new();
    public string? RecalcMessage { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool IncludeLive { get; set; } = false;

    public class EvaluationRow
    {
        public int Id { get; set; }
        public string UserId { get; set; } = "";
        public string UserDisplay { get; set; } = "";
        public DateTime EvalDateUtc { get; set; }
        public decimal AverageScore { get; set; }
        public DateTime RecordedAtUtc { get; set; }
        public string SummaryHtml { get; set; } = "";
        public bool IsLive { get; set; }
        // Detailed breakdown for diagnostics
        public int WorkSecondsSum { get; set; }
        public int BreakSecondsTotal { get; set; }
        public int BreakOverlapSeconds { get; set; }
        public int NetWorkedSeconds { get; set; }
        public int TaskSeconds { get; set; }
    }

    public async Task OnGetAsync()
    {
        await LoadPageAsync();
    }

    /// <summary>
    /// Re-fetches timeline data from the external API for evaluations in the current filter
    /// whose TaskMinutes is 0 (broken records), then recomputes and saves all scores + HTML.
    /// Only Admin role can trigger this.
    /// </summary>
    public async Task<IActionResult> OnPostRecalculateAsync()
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var today = DateTime.UtcNow.Date;
        // Default to current week (Monday .. Sunday)
        DateTime StartOfWeek(DateTime d)
        {
            var diff = (7 + (d.DayOfWeek - DayOfWeek.Monday)) % 7;
            return d.AddDays(-diff).Date;
        }

        var from = FromDate?.Date ?? StartOfWeek(today);
        var to   = ToDate?.Date   ?? StartOfWeek(today).AddDays(6);
        if (to < from) (from, to) = (to, from);

        var toExclusive = to.AddDays(1);
        var q = _context.DailyTimeEvaluations
            .Where(e => e.EvalDateUtc >= from && e.EvalDateUtc < toExclusive);
        if (!string.IsNullOrWhiteSpace(UserIdFilter))
            q = q.Where(e => e.UserId == UserIdFilter);

        var evals = await q.ToListAsync();
        var taskSecondsCache = new Dictionary<(DateTime Date, string UserId), int>();
        var dayMaxCache = new Dictionary<DateTime, int>();
        int updated = 0;
        foreach (var eval in evals)
        {
            var dateStart = eval.EvalDateUtc.Date;
            var dateEnd = dateStart.AddDays(1);
            var nowUtc = DateTime.UtcNow;

            // Recompute factory summary (worked seconds, breaks, entry/exit labels and scores)
            var (computedWorkedSeconds, computedBreakSeconds, computedEntryLabel, computedExitLabel, computedScoreEntry, computedScoreExit, workSecondsSum, overlapSeconds) =
                await ComputeFactorySummaryForDayAsync(eval.UserId, dateStart, dateEnd, nowUtc);

            // Recompute task seconds (MTI + external API merged)
            var taskSeconds = await GetTaskSecondsCachedAsync(taskSecondsCache, eval.UserId, dateStart, dateEnd, nowUtc);
            var taskMinutes = Math.Max(0, (int)Math.Round(taskSeconds / 60.0));

            const int FullDaySeconds = 7 * 3600 + 40 * 60; // 7:40 = 100%
            var taskPercent = Math.Min(100.0, 100.0 * taskSeconds / FullDaySeconds);
            double scoreTasks = taskPercent >= 90 ? 1.0 : taskPercent >= 75 ? 0.5 : 0.0;

            var netWorkedSeconds = Math.Max(0, computedWorkedSeconds);
            var scoreWorked = netWorkedSeconds >= FullDaySeconds ? 1.0 : netWorkedSeconds >= FullDaySeconds * 0.75 ? 0.5 : 0.0;

            double avg = (computedScoreEntry + computedScoreExit + scoreTasks + scoreWorked + (double)eval.ScoreBreak) / 5.0;

            var html = DailyEvaluationHtml.BuildEndOfDaySummaryHtml(
                computedScoreEntry, computedScoreExit, scoreTasks,
                scoreWorked, (double)eval.ScoreBreak,
                avg, computedEntryLabel, computedExitLabel,
                taskSeconds, netWorkedSeconds, computedBreakSeconds);

            eval.TaskMinutes = taskMinutes;
            eval.TaskPercent = (decimal)taskPercent;
            eval.ScoreTasks = (decimal)scoreTasks;
            eval.ScoreWorked = (decimal)scoreWorked;
            eval.WorkedSeconds = netWorkedSeconds;
            eval.BreakSeconds = computedBreakSeconds;
            eval.ScoreEntry = (decimal)computedScoreEntry;
            eval.ScoreExit = (decimal)computedScoreExit;
            eval.EntryLabel = computedEntryLabel;
            eval.ExitLabel = computedExitLabel;
            eval.AverageScore = (decimal)avg;
            eval.SummaryHtml = html;
            updated++;
        }

        await _context.SaveChangesAsync();

        TempData["RecalcMessage"] = $"Recalculated {updated} evaluations.";
        return RedirectToPage(new { FromDate = from.ToString("yyyy-MM-dd"), ToDate = to.ToString("yyyy-MM-dd"), UserIdFilter });
    }
    private async Task<int> CalculateInternalTaskMinutesAsync(string userId, DateTime dateStart, DateTime dateEnd, DateTime utcNow)
    {
        var taskMinutes = 0;

        var stepRuns = await _context.StepRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.StartedAt >= dateStart && r.StartedAt < dateEnd)
            .ToListAsync();
        foreach (var r in stepRuns)
        {
            var effectiveEnd = r.FinishedAt ?? (utcNow < dateEnd ? utcNow : dateEnd);
            if (effectiveEnd > dateEnd) effectiveEnd = dateEnd;
            var durationSeconds = (int)(effectiveEnd - r.StartedAt).TotalSeconds;
            taskMinutes += Math.Max(0, (int)Math.Round(durationSeconds / 60.0));
        }

        var taskSessions = await _context.TaskSessions
            .AsNoTracking()
            .Include(s => s.Pauses)
            .Where(s => s.UserId == userId && s.StartedAtUtc >= dateStart && s.StartedAtUtc < dateEnd)
            .ToListAsync();
        foreach (var s in taskSessions)
        {
            var openPause = s.Pauses.FirstOrDefault(p => p.EndUtc == null);
            var effectiveEnd = s.EndedAtUtc ?? (openPause != null ? openPause.StartUtc : (utcNow < dateEnd ? utcNow : dateEnd));
            if (effectiveEnd > dateEnd) effectiveEnd = dateEnd;
            var span = effectiveEnd - s.StartedAtUtc;
            var pauseTotal = TimeSpan.Zero;
            foreach (var p in s.Pauses)
            {
                var pauseStart = p.StartUtc;
                var pauseEnd = p.EndUtc ?? effectiveEnd;
                if (pauseStart >= effectiveEnd) continue;
                if (pauseEnd > effectiveEnd) pauseEnd = effectiveEnd;
                if (pauseEnd > pauseStart)
                    pauseTotal += pauseEnd - pauseStart;
            }
            var durationSeconds = Math.Max(0, (int)(span - pauseTotal).TotalSeconds);
            taskMinutes += Math.Max(0, (int)Math.Round(durationSeconds / 60.0));
        }

        return taskMinutes;
    }

    private async Task<int> GetTaskSecondsCachedAsync(
        Dictionary<(DateTime Date, string UserId), int> cache,
        string userId,
        DateTime dateStart,
        DateTime dateEnd,
        DateTime utcNow)
    {
        var key = (dateStart.Date, userId);
        if (cache.TryGetValue(key, out var value))
            return value;

        var internalSeconds = await CalculateInternalTaskSecondsAsync(userId, dateStart, dateEnd, utcNow);
        var apiMinutes = await FetchExternalTaskMinutesForUserDayAsync(userId, dateStart);
        // Merge MTI + API by taking the max to avoid double-counting the same work in both sources.
        var total = Math.Max(internalSeconds, Math.Max(0, apiMinutes) * 60);
        cache[key] = total;
        return total;
    }

    private async Task<int> CalculateMaxTaskSecondsForDayAsync(
        Dictionary<(DateTime Date, string UserId), int> cache,
        Dictionary<DateTime, int> dayMaxCache,
        DateTime dateStart,
        DateTime dateEnd,
        DateTime utcNow)
    {
        var day = dateStart.Date;
        if (dayMaxCache.TryGetValue(day, out var cachedMax))
            return cachedMax;

        var userIds = new HashSet<string>(StringComparer.Ordinal);

        var stepUserIds = await _context.StepRuns
            .AsNoTracking()
            .Where(r => r.StartedAt >= dateStart && r.StartedAt < dateEnd && !string.IsNullOrEmpty(r.UserId))
            .Select(r => r.UserId!)
            .Distinct()
            .ToListAsync();

        var taskUserIds = await _context.TaskSessions
            .AsNoTracking()
            .Where(s => s.StartedAtUtc >= dateStart && s.StartedAtUtc < dateEnd)
            .Select(s => s.UserId)
            .Distinct()
            .ToListAsync();

        var ttUserIds = await _context.TimeTrackingEntries
            .AsNoTracking()
            .Where(e => e.TimestampUtc >= dateStart && e.TimestampUtc < dateEnd)
            .Select(e => e.UserId)
            .Distinct()
            .ToListAsync();

        var evalUserIds = await _context.DailyTimeEvaluations
            .AsNoTracking()
            .Where(e => e.EvalDateUtc >= dateStart && e.EvalDateUtc < dateEnd)
            .Select(e => e.UserId)
            .Distinct()
            .ToListAsync();

        foreach (var id in stepUserIds.Concat(taskUserIds).Concat(ttUserIds).Concat(evalUserIds))
            userIds.Add(id);

        var baseUrl = _configuration["ExternalApi:BaseUrl"]?.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            var allIds = await _userManager.Users.AsNoTracking().Select(u => u.Id).ToListAsync();
            foreach (var id in allIds)
                userIds.Add(id);
        }

        var maxSeconds = 0;
        foreach (var uid in userIds)
        {
            var seconds = await GetTaskSecondsCachedAsync(cache, uid, dateStart, dateEnd, utcNow);
            if (seconds > maxSeconds) maxSeconds = seconds;
        }

        dayMaxCache[day] = maxSeconds;
        return maxSeconds;
    }

    private async Task<int> CalculateInternalTaskSecondsAsync(string userId, DateTime dateStart, DateTime dateEnd, DateTime utcNow)
    {
        var totalSeconds = 0;

        var stepRuns = await _context.StepRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.StartedAt >= dateStart && r.StartedAt < dateEnd)
            .ToListAsync();
        foreach (var r in stepRuns)
        {
            var effectiveEnd = r.FinishedAt ?? (utcNow < dateEnd ? utcNow : dateEnd);
            if (effectiveEnd > dateEnd) effectiveEnd = dateEnd;
            var durationSeconds = (int)(effectiveEnd - r.StartedAt).TotalSeconds;
            totalSeconds += Math.Max(0, durationSeconds);
        }

        var taskSessions = await _context.TaskSessions
            .AsNoTracking()
            .Include(s => s.Pauses)
            .Where(s => s.UserId == userId && s.StartedAtUtc >= dateStart && s.StartedAtUtc < dateEnd)
            .ToListAsync();
        foreach (var s in taskSessions)
        {
            var openPause = s.Pauses.FirstOrDefault(p => p.EndUtc == null);
            var effectiveEnd = s.EndedAtUtc ?? (openPause != null ? openPause.StartUtc : (utcNow < dateEnd ? utcNow : dateEnd));
            if (effectiveEnd > dateEnd) effectiveEnd = dateEnd;
            var span = effectiveEnd - s.StartedAtUtc;
            var pauseTotal = TimeSpan.Zero;
            foreach (var p in s.Pauses)
            {
                var pauseStart = p.StartUtc;
                var pauseEnd = p.EndUtc ?? effectiveEnd;
                if (pauseStart >= effectiveEnd) continue;
                if (pauseEnd > effectiveEnd) pauseEnd = effectiveEnd;
                if (pauseEnd > pauseStart)
                    pauseTotal += pauseEnd - pauseStart;
            }
            var durationSeconds = Math.Max(0, (int)(span - pauseTotal).TotalSeconds);
            totalSeconds += durationSeconds;
        }

        return totalSeconds;
    }


    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task LoadPageAsync()
    {
        var today = DateTime.UtcNow.Date;
        // Default to current week starting on Monday
        var daysFromMonday = ((int)today.DayOfWeek + 6) % 7; // Monday=0
        var thisMonday = today.AddDays(-daysFromMonday);
        var from = FromDate?.Date ?? thisMonday;
        var to   = ToDate?.Date   ?? today;
        if (to < from) (from, to) = (to, from);

        FromDate = from;
        ToDate   = to;

        RecalcMessage = TempData["RecalcMessage"] as string;

        var users = await _userManager.Users.AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new { u.Id, u.Email, u.UserName })
            .ToListAsync();
        var userList = users.Select(u => new SelectListItem
        {
            Value = u.Id,
            Text  = !string.IsNullOrEmpty(u.Email) ? u.Email : (u.UserName ?? u.Id)
        }).ToList();
        userList.Insert(0, new SelectListItem { Value = "", Text = "All users" });
        UserSelectList = new SelectList(userList, "Value", "Text", UserIdFilter);

        var toExclusive = to.AddDays(1);
        var q = _context.DailyTimeEvaluations.AsNoTracking()
            .Where(e => e.EvalDateUtc >= from && e.EvalDateUtc < toExclusive);
        if (!string.IsNullOrWhiteSpace(UserIdFilter))
            q = q.Where(e => e.UserId == UserIdFilter);

        var evals = await q.OrderByDescending(e => e.EvalDateUtc).ThenBy(e => e.UserId).ToListAsync();
        var idToName = users.ToDictionary(
            u => u.Id,
            u => !string.IsNullOrEmpty(u.Email) ? u.Email : (u.UserName ?? u.Id));

        var rowsByKey = new Dictionary<(string UserId, DateTime Day), EvaluationRow>();
        foreach (var e in evals)
        {
            var row = new EvaluationRow
            {
                Id           = e.Id,
                UserId       = e.UserId,
                UserDisplay  = idToName.TryGetValue(e.UserId, out var n) ? n : e.UserId,
                EvalDateUtc  = e.EvalDateUtc,
                AverageScore = e.AverageScore,
                RecordedAtUtc = e.RecordedAtUtc,
                SummaryHtml  = e.SummaryHtml,
                IsLive = false
            };
            rowsByKey[(row.UserId, row.EvalDateUtc.Date)] = row;
        }

        // Real-time coverage:
        // - Add missing user/day rows that have activity but no saved snapshot yet.
        // - Replace today's saved snapshots with live values so "status now" is visible immediately.
        var fromInclusive = from.Date;
        var toExclusiveRange = to.Date.AddDays(1);
        var nowUtc = DateTime.UtcNow;
        var todayUtc = nowUtc.Date;

        var activityKeys = new HashSet<(string UserId, DateTime Day)>();

        var ttActivity = await _context.TimeTrackingEntries.AsNoTracking()
            .Where(e => e.TimestampUtc >= fromInclusive && e.TimestampUtc < toExclusiveRange)
            .Select(e => new { e.UserId, e.TimestampUtc })
            .ToListAsync();
        foreach (var a in ttActivity)
            activityKeys.Add((a.UserId, a.TimestampUtc.Date));

        var runActivity = await _context.StepRuns.AsNoTracking()
            .Where(r => r.StartedAt >= fromInclusive && r.StartedAt < toExclusiveRange && !string.IsNullOrEmpty(r.UserId))
            .Select(r => new { UserId = r.UserId!, r.StartedAt })
            .ToListAsync();
        foreach (var a in runActivity)
            activityKeys.Add((a.UserId, a.StartedAt.Date));

        var taskActivity = await _context.TaskSessions.AsNoTracking()
            .Where(s => s.StartedAtUtc >= fromInclusive && s.StartedAtUtc < toExclusiveRange)
            .Select(s => new { s.UserId, s.StartedAtUtc })
            .ToListAsync();
        foreach (var a in taskActivity)
            activityKeys.Add((a.UserId, a.StartedAtUtc.Date));

        if (!string.IsNullOrWhiteSpace(UserIdFilter))
            activityKeys.RemoveWhere(k => k.UserId != UserIdFilter);

        var taskSecondsCache = new Dictionary<(DateTime Date, string UserId), int>();
        var dayMaxCache = new Dictionary<DateTime, int>();
        foreach (var key in activityKeys)
        {
            var shouldUseLive = key.Day == todayUtc || !rowsByKey.ContainsKey(key);
            if (!shouldUseLive)
                continue;

            var live = await BuildLiveEvaluationRowAsync(key.UserId, key.Day, idToName, taskSecondsCache, dayMaxCache, nowUtc);
            rowsByKey[key] = live;
        }

        var evalById = evals.ToDictionary(e => e.Id);
        foreach (var row in rowsByKey.Values.ToList())
        {
            if (row.Id > 0 && evalById.TryGetValue(row.Id, out var savedEval))
                await RefreshSavedRowDisplayAsync(row, savedEval, taskSecondsCache, dayMaxCache, nowUtc);
        }

        var ordered = rowsByKey.Values
            .OrderByDescending(r => r.EvalDateUtc)
            .ThenBy(r => r.UserDisplay, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Pagination
        TotalRows = ordered.Count;
        TotalPages = (int)Math.Ceiling(TotalRows / (double)PageSize);
        if (Page < 1) Page = 1;
        if (Page > TotalPages) Page = Math.Max(1, TotalPages);
        Rows = ordered.Skip((Page - 1) * PageSize).Take(PageSize).ToList();
    }

    private async Task RefreshSavedRowDisplayAsync(
        EvaluationRow row,
        DailyTimeEvaluation saved,
        Dictionary<(DateTime Date, string UserId), int> taskSecondsCache,
        Dictionary<DateTime, int> dayMaxCache,
        DateTime nowUtc)
    {
        var dateStart = saved.EvalDateUtc.Date;
        var dateEnd = dateStart.AddDays(1);

        // Recompute factory summary to get accurate breakdowns
        var (computedWorkedSeconds, computedBreakSeconds, computedEntryLabel, computedExitLabel, computedScoreEntry, computedScoreExit, workSecondsSum, overlapSeconds) =
            await ComputeFactorySummaryForDayAsync(saved.UserId, dateStart, dateEnd, nowUtc);

        var taskSeconds = await GetTaskSecondsCachedAsync(taskSecondsCache, saved.UserId, dateStart, dateEnd, nowUtc);

        const int FullDaySeconds = 7 * 3600 + 40 * 60;
        var taskPct = Math.Min(100.0, 100.0 * taskSeconds / FullDaySeconds);
        var scoreTasks = taskPct >= 90 ? 1.0 : taskPct >= 75 ? 0.5 : 0.0;

        var netWorkedSeconds = Math.Max(0, computedWorkedSeconds);
        var scoreWorked = netWorkedSeconds >= FullDaySeconds ? 1.0 : netWorkedSeconds >= FullDaySeconds * 0.75 ? 0.5 : 0.0;

        var avg = (computedScoreEntry + computedScoreExit + scoreTasks + scoreWorked + (double)saved.ScoreBreak) / 5.0;

        row.AverageScore = (decimal)avg;
        row.SummaryHtml = DailyEvaluationHtml.BuildEndOfDaySummaryHtml(
            computedScoreEntry, computedScoreExit, scoreTasks,
            scoreWorked, (double)saved.ScoreBreak,
            avg, computedEntryLabel, computedExitLabel,
            taskSeconds, netWorkedSeconds, computedBreakSeconds);

        // Fill diagnostic fields
        row.WorkSecondsSum = workSecondsSum;
        row.BreakSecondsTotal = computedBreakSeconds;
        row.BreakOverlapSeconds = overlapSeconds;
        row.NetWorkedSeconds = netWorkedSeconds;
        row.TaskSeconds = taskSeconds;
    }

    private async Task<EvaluationRow> BuildLiveEvaluationRowAsync(
        string userId,
        DateTime dayUtc,
        Dictionary<string, string> idToName,
        Dictionary<(DateTime Date, string UserId), int> taskSecondsCache,
        Dictionary<DateTime, int> dayMaxCache,
        DateTime nowUtc)
    {
        var dayStart = dayUtc.Date;
        var dayEnd = dayStart.AddDays(1);

        var (workedSeconds, breakSeconds, entryLabel, exitLabel, scoreEntry, scoreExit, workSecondsSum, overlapSeconds) =
            await ComputeFactorySummaryForDayAsync(userId, dayStart, dayEnd, nowUtc);

        var taskSeconds = await GetTaskSecondsCachedAsync(taskSecondsCache, userId, dayStart, dayEnd, nowUtc);

        const int FullDaySeconds = 7 * 3600 + 40 * 60; // 7:40 = 100%
        const int BreakTargetSeconds = 20 * 60;
        var taskPct = Math.Min(100.0, 100.0 * taskSeconds / FullDaySeconds);

        var scoreTasks = taskPct >= 90 ? 1.0 : taskPct >= 75 ? 0.5 : 0.0;
        // 'workedSeconds' returned by ComputeFactorySummaryForDayAsync is already net (work minus break overlap).
        var netWorkedSeconds = Math.Max(0, workedSeconds);
        var scoreWorked = netWorkedSeconds >= FullDaySeconds ? 1.0 : netWorkedSeconds >= FullDaySeconds * 0.75 ? 0.5 : 0.0;
        var scoreBreak = breakSeconds <= BreakTargetSeconds ? 1.0 : breakSeconds <= BreakTargetSeconds * 2 ? 0.5 : 0.0;
        var avg = (scoreEntry + scoreExit + scoreTasks + scoreWorked + scoreBreak) / 5.0;

        var html = DailyEvaluationHtml.BuildEndOfDaySummaryHtml(
            scoreEntry, scoreExit, scoreTasks, scoreWorked, scoreBreak,
            avg, entryLabel, exitLabel, taskSeconds, netWorkedSeconds, breakSeconds);

        return new EvaluationRow
        {
            Id = 0,
            UserId = userId,
            UserDisplay = idToName.TryGetValue(userId, out var name) ? name : userId,
            EvalDateUtc = dayStart,
            AverageScore = (decimal)avg,
            RecordedAtUtc = nowUtc,
            SummaryHtml = html,
            WorkSecondsSum = workSecondsSum,
            BreakSecondsTotal = breakSeconds,
            BreakOverlapSeconds = overlapSeconds,
            NetWorkedSeconds = Math.Max(0, workedSeconds),
            TaskSeconds = taskSeconds,
            IsLive = true
        };
    }

    private async Task<(int workedSeconds, int breakSeconds, string entryLabel, string exitLabel, double scoreEntry, double scoreExit, int workSecondsSum, int overlapSeconds)>
        ComputeFactorySummaryForDayAsync(string userId, DateTime dayStart, DateTime dayEnd, DateTime nowUtc)
    {
        var entries = await _context.TimeTrackingEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.TimestampUtc >= dayStart && e.TimestampUtc < dayEnd)
            .OrderBy(e => e.TimestampUtc)
            .ToListAsync();

        var worked = TimeSpan.Zero;
        var breakTime = TimeSpan.Zero;
        DateTime? breakStartTime = null;
        // Collect breaks and first/last timestamps
        DateTime? firstClockIn = entries.FirstOrDefault(e => e.ActionType == "CLOCK_IN")?.TimestampUtc;
        DateTime? lastClockOut = entries.LastOrDefault(e => e.ActionType == "CLOCK_OUT")?.TimestampUtc;

        // Build lists of work segments (paired CLOCK_IN -> CLOCK_OUT) and break segments
        var workSegments = new List<(DateTime Start, DateTime End)>();
        DateTime? currentIn = null;
        var nowOrDayEnd = nowUtc < dayEnd ? nowUtc : dayEnd;
        foreach (var e in entries)
        {
            if (e.ActionType == "CLOCK_IN")
            {
                currentIn = e.TimestampUtc;
            }
            else if (e.ActionType == "CLOCK_OUT")
            {
                if (currentIn.HasValue)
                {
                    var segStart = currentIn.Value;
                    var segEnd = e.TimestampUtc;
                    if (segStart < dayStart) segStart = dayStart;
                    if (segEnd > nowOrDayEnd) segEnd = nowOrDayEnd;
                    if (segEnd > segStart) workSegments.Add((segStart, segEnd));
                    currentIn = null;
                }
            }
        }
        if (currentIn.HasValue)
        {
            var segStart = currentIn.Value;
            var segEnd = nowOrDayEnd;
            if (segStart < dayStart) segStart = dayStart;
            if (segEnd > segStart) workSegments.Add((segStart, segEnd));
        }

        var breakSegments = new List<(DateTime Start, DateTime End)>();
        breakStartTime = null;
        foreach (var e in entries)
        {
            if (e.ActionType == "BREAK_START") breakStartTime = e.TimestampUtc;
            else if (e.ActionType == "BREAK_END")
            {
                if (breakStartTime.HasValue)
                {
                    var bStart = breakStartTime.Value;
                    var bEnd = e.TimestampUtc;
                    if (bStart < dayStart) bStart = dayStart;
                    if (bEnd > nowOrDayEnd) bEnd = nowOrDayEnd;
                    if (bEnd > bStart) breakSegments.Add((bStart, bEnd));
                    breakStartTime = null;
                }
            }
        }
        // open break
        if (breakStartTime.HasValue)
        {
            var bStart = breakStartTime.Value;
            var bEnd = nowOrDayEnd;
            if (bStart < dayStart) bStart = dayStart;
            if (bEnd > bStart) breakSegments.Add((bStart, bEnd));
        }

        // Sum total break seconds (for display) and compute overlap with work segments
        var totalBreakSeconds = 0L;
        foreach (var b in breakSegments)
            totalBreakSeconds += (long)(b.End - b.Start).TotalSeconds;

        var workSecondsSum = 0L;
        foreach (var w in workSegments)
            workSecondsSum += (long)(w.End - w.Start).TotalSeconds;

        long overlapSeconds = 0L;
        foreach (var w in workSegments)
        {
            foreach (var b in breakSegments)
            {
                var s = w.Start > b.Start ? w.Start : b.Start;
                var e2 = w.End < b.End ? w.End : b.End;
                if (e2 > s) overlapSeconds += (long)(e2 - s).TotalSeconds;
            }
        }

        // Net worked seconds = sum of work segments minus break overlap seconds
        var netWorked = workSecondsSum - overlapSeconds;
        if (netWorked < 0) netWorked = 0;
        worked = TimeSpan.FromSeconds(netWorked);
        breakTime = TimeSpan.FromSeconds(totalBreakSeconds);

        var entryLabel = firstClockIn.HasValue ? firstClockIn.Value.ToLocalTime().ToString("HH:mm") : "No hay registro de entrada";
        var exitLabel = lastClockOut.HasValue ? lastClockOut.Value.ToLocalTime().ToString("HH:mm") : "No hay registro de salida";
        var scoreEntry = firstClockIn.HasValue && firstClockIn.Value.ToLocalTime().TimeOfDay <= TimeSpan.FromHours(7) ? 1.0 : 0.0;
        var scoreExit = lastClockOut.HasValue && lastClockOut.Value.ToLocalTime().TimeOfDay >= TimeSpan.FromHours(15) ? 1.0 : 0.0;

        return ((int)worked.TotalSeconds, (int)breakTime.TotalSeconds, entryLabel, exitLabel, scoreEntry, scoreExit, (int)workSecondsSum, (int)overlapSeconds);
    }

    private async Task<int> FetchExternalTaskMinutesForUserDayAsync(string userId, DateTime dateUtc)
    {
        var baseUrl = _configuration["ExternalApi:BaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)) return 0;

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return 0;

        var apiKey     = _configuration["ExternalApi:ApiKey"];
        var hostHeader = _configuration["ExternalApi:HostHeader"];
        try
        {
            using var client = CreateExternalApiHttpClient();
            if (!string.IsNullOrEmpty(apiKey))
                client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            foreach (var candidate in GetUsernameCandidates(user.Email, user.UserName))
            {
                var requestUrl =
                    $"{baseUrl}/api/TecnicosApi/Timeline?user={Uri.EscapeDataString(candidate)}&date={dateUtc:yyyy-MM-dd}";
                using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                if (!string.IsNullOrEmpty(hostHeader))
                    req.Headers.Host = hostHeader;

                using var response = await client.SendAsync(req);
                if (!response.IsSuccessStatusCode)
                    continue;

                var json = await response.Content.ReadAsStringAsync();
                var parsed = ParseTimelineResult(json);
                if (parsed != null && parsed.TaskMinutes > 0)
                    return parsed.TaskMinutes;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Timeline API user-day fetch failed for userId {UserId}", userId);
        }

        return 0;
    }

    /// <summary>Returns username candidates to try against Websat (most specific first).</summary>
    private static IEnumerable<string> GetUsernameCandidates(string? email, string? userName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> Yield(string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) yield break;
            v = v.Trim();
            if (seen.Add(v)) yield return v;
            var at = v.IndexOf('@');
            if (at > 0)
            {
                var prefix = v[..at];
                if (seen.Add(prefix)) yield return prefix;
            }
        }
        return Yield(email).Concat(Yield(userName));
    }

    private HttpClient CreateExternalApiHttpClient()
    {
        var dangerousCert = _configuration.GetValue<bool>("ExternalApi:DangerousAcceptAnyServerCertificate");
        var hostHeader    = _configuration["ExternalApi:HostHeader"];
        var skipSsl = dangerousCert || !string.IsNullOrEmpty(hostHeader);
        if (skipSsl)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            return new HttpClient(handler);
        }
        return new HttpClient();
    }

    private async Task<TimelineResult?> FetchTimelineMetricsAsync(string externalUser, DateTime dateUtc)
    {
        var baseUrl = _configuration["ExternalApi:BaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;

        var apiKey     = _configuration["ExternalApi:ApiKey"];
        var hostHeader = _configuration["ExternalApi:HostHeader"];
        var requestUrl = $"{baseUrl}/api/TecnicosApi/Timeline?user={Uri.EscapeDataString(externalUser)}&date={dateUtc:yyyy-MM-dd}";

        try
        {
            using var client = CreateExternalApiHttpClient();
            if (!string.IsNullOrEmpty(apiKey))
                client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            if (!string.IsNullOrEmpty(hostHeader))
                req.Headers.Host = hostHeader;

            using var response = await client.SendAsync(req);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Timeline API returned {Status} for user {User} date {Date}",
                    response.StatusCode, externalUser, dateUtc.ToString("yyyy-MM-dd"));
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return ParseTimelineResult(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Timeline API call failed for user {User} date {Date}",
                externalUser, dateUtc.ToString("yyyy-MM-dd"));
            return null;
        }
    }

    private static TimelineResult? ParseTimelineResult(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            var root = doc.RootElement;

            // Try pre-aggregated field first.
            if (TryGetPropIC(root, "taskMinutes", out var tmProp) &&
                tmProp.ValueKind == JsonValueKind.Number &&
                tmProp.TryGetInt32(out var directMinutes))
            {
                return new TimelineResult(directMinutes);
            }

            // Fall back to timeline ranges; merge overlaps to avoid double-counting.
            if (TryGetPropIC(root, "timeline", out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
            {
                var ranges = new List<(DateTimeOffset Start, DateTimeOffset End)>();
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (TryReadDateTimeOffset(item, out var start, "start", "from") &&
                        TryReadDateTimeOffset(item, out var end,   "end",   "to") &&
                        end > start)
                    {
                        ranges.Add((start, end));
                    }
                }
                return new TimelineResult(CalculateMergedTimelineMinutes(ranges));
            }

            return null;
        }
        catch (JsonException) { return null; }
    }

    private static bool TryGetPropIC(JsonElement el, string name, out JsonElement value)
    {
        foreach (var p in el.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            { value = p.Value; return true; }
        }
        value = default;
        return false;
    }

    private static bool TryReadDateTimeOffset(JsonElement el, out DateTimeOffset value, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetPropIC(el, name, out var prop) &&
                prop.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(prop.GetString(), out value))
            {
                return true;
            }
        }
        value = default;
        return false;
    }

    private static int CalculateMergedTimelineMinutes(List<(DateTimeOffset Start, DateTimeOffset End)> ranges)
    {
        if (ranges.Count == 0) return 0;
        var ordered = ranges
            .OrderBy(r => r.Start)
            .ThenBy(r => r.End)
            .ToList();

        var mergedMinutes = 0.0;
        var currentStart = ordered[0].Start;
        var currentEnd = ordered[0].End;

        for (var i = 1; i < ordered.Count; i++)
        {
            var next = ordered[i];
            if (next.Start <= currentEnd)
            {
                if (next.End > currentEnd) currentEnd = next.End;
                continue;
            }

            mergedMinutes += (currentEnd - currentStart).TotalMinutes;
            currentStart = next.Start;
            currentEnd = next.End;
        }

        mergedMinutes += (currentEnd - currentStart).TotalMinutes;
        return Math.Max(0, (int)Math.Round(mergedMinutes));
    }

    private sealed record TimelineResult(int TaskMinutes);
}
