using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models.Execution;
using System.Linq;

namespace ManufacturingTimeTracking.Pages.Reports;

[Authorize(Roles = "Admin,Supervisor")]
public class BlockStatsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    public BlockStatsModel(ApplicationDbContext db) { _db = db; }

    [BindProperty(SupportsGet = true)] public DateTime? FromDate { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? ToDate { get; set; }

    // Summary
    public int TotalBlocks { get; set; }
    public double AverageDurationSeconds { get; set; }
    public int MedianDurationSeconds { get; set; }
    public object TopReasons { get; set; } = new { labels = new string[0], values = new int[0] };

    // Weekly buckets
    public object WeeklySummary { get; set; } = new { labels = new string[0], values = new int[0] };
    public object WeeklyDetails { get; set; } = new object();

    // Diagnostics
    public int Scanned { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // default last 12 weeks
        const int defaultWeeks = 12;
        var to = ToDate?.Date.AddDays(1) ?? DateTime.UtcNow.Date.AddDays(1);
        var from = FromDate?.Date ?? to.AddDays(-7 * defaultWeeks);

        // load interruptions in window by starttime OR by endtime overlapping window
        var lists = await _db.ProductionInterruptions.AsNoTracking()
            .Include(pi => pi.BuildOrder)
            .Where(pi => pi.Category == ProductionInterruptionCategory.Blocked && ((pi.StartTime >= from && pi.StartTime < to) || (pi.EndTime != null && pi.EndTime >= from && pi.EndTime < to)))
            .OrderByDescending(pi => pi.StartTime)
            .ToListAsync();

        Scanned = lists.Count;

        // compute durations and collect values
        var durations = new List<int>();
        var rows = new List<(ProductionInterruption pi, int durationSeconds)>();
        var now = DateTime.UtcNow;
        foreach (var pi in lists)
        {
            var end = pi.EndTime ?? now;
            var dur = (int)(end - pi.StartTime).TotalSeconds;
            if (dur < 0) dur = 0;
            durations.Add(dur);
            rows.Add((pi, dur));
        }

        TotalBlocks = rows.Count;
        AverageDurationSeconds = durations.Any() ? durations.Average() : 0;
        MedianDurationSeconds = durations.Any() ? durations.OrderBy(x => x).ElementAt(durations.Count / 2) : 0;

        // top reasons by notes / blockedreason name
        var reasonGroups = rows.GroupBy(r => string.IsNullOrWhiteSpace(r.pi.Notes) ? (r.pi.BuildOrder?.BlockedReason?.Name ?? "(no reason)") : r.pi.Notes)
            .OrderByDescending(g => g.Count()).Take(10).ToDictionary(g => g.Key, g => g.Count());
        TopReasons = new { labels = reasonGroups.Keys.ToArray(), values = reasonGroups.Values.ToArray() };

        // build weekly buckets between from..to using start time
        var totalDays = (to.Date - from.Date).TotalDays;
        var weeks = Math.Max(1, (int)Math.Ceiling(totalDays / 7.0));
        var weekLabels = new List<string>();
        var weekCounts = new int[weeks];
        var weekDetails = new List<List<object>>();
        for (int i = 0; i < weeks; i++) { weekDetails.Add(new List<object>()); var s = from.AddDays(7 * i); weekLabels.Add(s.ToString("yyyy-MM-dd")); }

        // resolve user display names for creators
        var userIds = rows.Select(r => r.pi.CreatedByUserId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
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

        foreach (var (pi, dur) in rows)
        {
            // pick bucket by StartTime
            var dt = pi.StartTime.ToUniversalTime().Date;
            var diff = (dt - from.Date).TotalDays;
            if (diff < 0) continue;
            var idx = (int)(diff / 7);
            if (idx < 0 || idx >= weeks) continue;
            weekCounts[idx]++;
            var label = !string.IsNullOrWhiteSpace(pi.BuildOrder?.ExternalRef) ? pi.BuildOrder.ExternalRef : (pi.BuildOrder?.SerialNumber ?? ("Order " + pi.BuildOrderId));
            var createdBy = string.IsNullOrWhiteSpace(pi.CreatedByUserId) ? "(unknown)" : (userMap.ContainsKey(pi.CreatedByUserId) ? userMap[pi.CreatedByUserId] : pi.CreatedByUserId);
            weekDetails[idx].Add(new { OrderId = pi.BuildOrderId, Label = label, Duration = dur, Start = pi.StartTime, End = pi.EndTime, Reason = pi.Notes ?? pi.BuildOrder?.BlockedReason?.Name ?? "(no reason)", CreatedBy = createdBy });
        }

        WeeklySummary = new { labels = weekLabels.ToArray(), values = weekCounts };
        WeeklyDetails = weekDetails.Select(w => w.ToArray()).ToArray();

        return Page();
    }
}
