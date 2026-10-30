using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Models.TimeTracking;
using ManufacturingTimeTracking.Models.Tasks;

namespace ManufacturingTimeTracking.Pages.TimeTracking;

[Authorize(Policy = "CanUseTimeManagement")]
public class ReportModel : PageModel
{
    private const string ActionClockIn = "CLOCK_IN";
    private const string ActionClockOut = "CLOCK_OUT";
    private const string ActionBreakStart = "BREAK_START";
    private const string ActionBreakEnd = "BREAK_END";

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public ReportModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? UserIdFilter { get; set; }

    public List<TimeTrackingReportRow> Rows { get; set; } = new();
    public Dictionary<string, string> UserDisplayNames { get; set; } = new();
    public SelectList? UserSelectList { get; set; }
    
    // Comparison rows: per task (step title) list users with average and runs
    public List<CompareRow> CompareRows { get; set; } = new();

    public class CompareRow
    {
        public string StepTitle { get; set; } = "";
        public List<UserTaskStat> UserStats { get; set; } = new();
    }

    public class UserTaskStat
    {
        public string UserId { get; set; } = "";
        public string UserDisplayName { get; set; } = "";
        public int Runs { get; set; }
        public int TotalSeconds { get; set; }
        public int AverageSeconds => Runs > 0 ? TotalSeconds / Runs : 0;
    }

    public class TimeTrackingReportRow
    {
        public string UserId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public TimeSpan TotalWorked { get; set; }
        public TimeSpan TotalBreak { get; set; }
        public TimeSpan TotalTaskTime { get; set; }
        public TimeSpan EffectiveWork => TotalWorked - TotalBreak;
        public TimeSpan EffectiveWorkIncludingTasks => TotalWorked - TotalBreak + TotalTaskTime;
        public int SessionCount { get; set; }
        public int BreakCount { get; set; }
    }

    public static string FormatDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1)
            return $"{t.Minutes}m";
        return "0m";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var from = FromDate ?? DateTime.UtcNow.Date.AddDays(-7);
        var to = ToDate ?? DateTime.UtcNow.Date;
        if (to < from) to = from.AddDays(1);

        var query = _context.TimeTrackingEntries
            .AsNoTracking()
            .Where(e => e.TimestampUtc >= from && e.TimestampUtc < to.AddDays(1));

        if (!string.IsNullOrWhiteSpace(UserIdFilter))
            query = query.Where(e => e.UserId == UserIdFilter);

        var entries = await query
            .OrderBy(e => e.UserId)
            .ThenBy(e => e.TimestampUtc)
            .ToListAsync();

        var userIds = entries.Select(e => e.UserId).Distinct().ToList();
        UserDisplayNames = new Dictionary<string, string>();
        foreach (var uid in userIds)
        {
            var user = await _userManager.FindByIdAsync(uid);
            UserDisplayNames[uid] = user?.UserName ?? user?.Email ?? uid;
        }

        var reportEndUtc = to.Date.AddDays(1);
        var effectiveNow = reportEndUtc < DateTime.UtcNow ? reportEndUtc : DateTime.UtcNow;

        var fromUtc = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Utc);
        var taskSessions = await _context.TaskSessions
            .AsNoTracking()
            .Include(s => s.Pauses)
            .Where(s => s.StartedAtUtc >= fromUtc && s.StartedAtUtc < toUtc)
            .Where(s => string.IsNullOrWhiteSpace(UserIdFilter) || s.UserId == UserIdFilter)
            .ToListAsync();
        var taskTimeByUser = new Dictionary<string, TimeSpan>();
        foreach (var s in taskSessions)
        {
            var openPause = s.Pauses.FirstOrDefault(p => p.EndUtc == null);
            var effectiveEnd = s.EndedAtUtc ?? (openPause != null ? openPause.StartUtc : (effectiveNow < toUtc ? effectiveNow : toUtc));
            var span = effectiveEnd - s.StartedAtUtc;
            var pauseTotal = s.Pauses.Where(p => p.EndUtc.HasValue && p.EndUtc.Value <= effectiveEnd).Sum(p => (p.EndUtc!.Value - p.StartUtc).Ticks);
            var duration = TimeSpan.FromTicks(span.Ticks - pauseTotal);
            taskTimeByUser.TryGetValue(s.UserId, out var existing);
            taskTimeByUser[s.UserId] = existing + duration;
        }
        var allUserIdsForRows = userIds.Union(taskTimeByUser.Keys).Distinct().ToList();
        foreach (var uid in taskTimeByUser.Keys.Where(uid => !UserDisplayNames.ContainsKey(uid)))
        {
            var user = await _userManager.FindByIdAsync(uid);
            UserDisplayNames[uid] = user?.UserName ?? user?.Email ?? uid;
        }

        Rows = new List<TimeTrackingReportRow>();
        foreach (var userId in allUserIdsForRows)
        {
            var userEntries = entries.Where(e => e.UserId == userId).OrderBy(e => e.TimestampUtc).ToList();
            var (worked, breakTime, sessions, breaks) = ComputeTotals(userEntries, effectiveNow);
            var taskTime = taskTimeByUser.GetValueOrDefault(userId, TimeSpan.Zero);
            Rows.Add(new TimeTrackingReportRow
            {
                UserId = userId,
                DisplayName = UserDisplayNames.GetValueOrDefault(userId, userId),
                TotalWorked = worked,
                TotalBreak = breakTime,
                TotalTaskTime = taskTime,
                SessionCount = sessions,
                BreakCount = breaks
            });
        }

        Rows = Rows.OrderByDescending(r => r.EffectiveWorkIncludingTasks).ToList();

        // Build compare rows: per step title, compute runs per user and average seconds
        var stepRuns = await _context.StepRuns
            .AsNoTracking()
            .Include(r => r.StepExec)
            .Where(r => r.StartedAt >= fromUtc && r.StartedAt < toUtc)
            .Where(r => r.FinishedAt != null)
            .ToListAsync();

        var grouped = stepRuns.GroupBy(r => r.StepExec?.Title ?? "(no title)")
            .Select(g => new CompareRow
            {
                StepTitle = g.Key ?? "(no title)",
                UserStats = g.GroupBy(r => r.UserId ?? "")
                    .Select(ug => new UserTaskStat
                    {
                        UserId = ug.Key,
                        UserDisplayName = UserDisplayNames.GetValueOrDefault(ug.Key, ug.Key),
                        Runs = ug.Count(),
                        TotalSeconds = ug.Sum(x => x.DurationSeconds ?? (int)((x.FinishedAt!.Value - x.StartedAt).TotalSeconds))
                    })
                    .OrderByDescending(us => us.AverageSeconds)
                    .ThenByDescending(us => us.Runs)
                    .ToList()
            })
            .ToList();

        CompareRows = grouped.OrderBy(r => r.StepTitle).ToList();

        var allUserIds = (await _context.TimeTrackingEntries.Select(e => e.UserId).Distinct().ToListAsync())
            .Union(await _context.TaskSessions.Select(s => s.UserId).Distinct().ToListAsync())
            .Distinct()
            .ToList();
        var userItems = new List<SelectListItem> { new SelectListItem("All users", "") };
        foreach (var uid in allUserIds.OrderBy(x => x))
        {
            var name = await _userManager.FindByIdAsync(uid);
            userItems.Add(new SelectListItem(name?.UserName ?? name?.Email ?? uid, uid));
        }
        UserSelectList = new SelectList(userItems, "Value", "Text", UserIdFilter);

        return Page();
    }

    private static (TimeSpan worked, TimeSpan breakTime, int sessions, int breaks) ComputeTotals(List<TimeTrackingEntry> ordered, DateTime effectiveNowUtc)
    {
        var worked = TimeSpan.Zero;
        var breakTime = TimeSpan.Zero;
        var sessions = 0;
        var breaks = 0;
        DateTime? clockInTime = null;
        DateTime? breakStartTime = null;

        foreach (var e in ordered)
        {
            if (e.ActionType == ActionClockIn)
            {
                clockInTime = e.TimestampUtc;
                sessions++;
            }
            else if (e.ActionType == ActionClockOut)
            {
                if (clockInTime.HasValue)
                {
                    worked += e.TimestampUtc - clockInTime.Value;
                    if (breakStartTime.HasValue)
                    {
                        breakTime += e.TimestampUtc - breakStartTime.Value;
                        breakStartTime = null;
                    }
                }
                clockInTime = null;
            }
            else if (e.ActionType == ActionBreakStart)
            {
                breakStartTime = e.TimestampUtc;
                breaks++;
            }
            else if (e.ActionType == ActionBreakEnd)
            {
                if (breakStartTime.HasValue)
                {
                    breakTime += e.TimestampUtc - breakStartTime.Value;
                    breakStartTime = null;
                }
            }
        }

        // Include in-progress session: time from last clock-in to "now" so report shows current work before clock-out
        if (clockInTime.HasValue && effectiveNowUtc > clockInTime.Value)
        {
            worked += effectiveNowUtc - clockInTime.Value;
            if (breakStartTime.HasValue && effectiveNowUtc > breakStartTime.Value)
                breakTime += effectiveNowUtc - breakStartTime.Value;
        }

        return (worked, breakTime, sessions, breaks);
    }
}
