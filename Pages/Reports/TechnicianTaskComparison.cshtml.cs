using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;

namespace ManufacturingTimeTracking.Pages.Reports;

[Authorize(Policy = "CanUseTimeManagement")]
public class TechnicianTaskComparisonModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public TechnicianTaskComparisonModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }
    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }
    [BindProperty(SupportsGet = true)]
    public string? UserIdFilter { get; set; }

    public SelectList? UserSelectList { get; set; }

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

    public async Task<IActionResult> OnGetAsync()
    {
        var from = FromDate ?? DateTime.UtcNow.Date.AddDays(-7);
        var to = ToDate ?? DateTime.UtcNow.Date;
        if (to < from) to = from;

        var fromUtc = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Utc);

        var stepRunsQuery = _db.StepRuns.AsNoTracking()
            .Include(r => r.StepExec)
            .Where(r => r.StartedAt >= fromUtc && r.StartedAt < toUtc && r.FinishedAt != null);

        if (!string.IsNullOrWhiteSpace(UserIdFilter))
            stepRunsQuery = stepRunsQuery.Where(r => r.UserId == UserIdFilter);

        var stepRuns = await stepRunsQuery.ToListAsync();

        var userIds = stepRuns.Select(r => r.UserId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var userDisplay = new Dictionary<string, string>();
        foreach (var uid in userIds)
        {
            var user = await _userManager.FindByIdAsync(uid);
            var display = user?.UserName ?? user?.Email ?? uid;
            var at = display.IndexOf('@');
            if (at > 0) display = display[..at];
            userDisplay[uid] = display;
        }

        static string NormalizeTitle(string? t)
        {
            if (string.IsNullOrWhiteSpace(t)) return "(no title)";
            // collapse whitespace and lowercase for grouping
            var s = System.Text.RegularExpressions.Regex.Replace(t.Trim(), @"\s+", " ");
            return s.ToLowerInvariant();
        }

        var grouped = stepRuns.GroupBy(r => NormalizeTitle(r.StepExec?.Title))
            .Select(g => new CompareRow
            {
                // choose a representative (first) original title for display
                StepTitle = g.Select(x => x.StepExec?.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "(no title)",
                UserStats = g.GroupBy(r => r.UserId ?? "")
                    .Select(ug => new UserTaskStat
                    {
                        UserId = ug.Key,
                        UserDisplayName = userDisplay.GetValueOrDefault(ug.Key, ug.Key),
                        Runs = ug.Count(),
                        TotalSeconds = ug.Sum(x => x.DurationSeconds ?? (int)((x.FinishedAt!.Value - x.StartedAt).TotalSeconds))
                    })
                    .OrderByDescending(us => us.AverageSeconds)
                    .ThenByDescending(us => us.Runs)
                    .ToList()
            })
            .OrderBy(r => r.StepTitle)
            .ToList();

        CompareRows = grouped;

        var allUserIds = await _db.StepRuns.Select(r => r.UserId).Distinct().ToListAsync();
        var userItems = new List<SelectListItem> { new SelectListItem("— All users —", "") };
        foreach (var uid in allUserIds.Where(x => !string.IsNullOrEmpty(x)).OrderBy(x => x))
        {
            var u = await _userManager.FindByIdAsync(uid);
            var name = u?.UserName ?? u?.Email ?? uid;
            userItems.Add(new SelectListItem(name, uid));
        }
        UserSelectList = new SelectList(userItems, "Value", "Text", UserIdFilter);

        return Page();
    }
}
