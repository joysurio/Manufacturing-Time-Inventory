using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using Microsoft.AspNetCore.Identity;
using ManufacturingTimeTracking.Models.TimeTracking;
using ManufacturingTimeTracking.Models;

namespace ManufacturingTimeTracking.Pages.TimeTracking;

[Authorize(Roles = "Admin,Supervisor")]
public class DailyReportModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public DailyReportModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }
    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }
    [BindProperty(SupportsGet = true)]
    public string? SelectedUserId { get; set; }
    public SelectList? UserSelectList { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SelectedAction { get; set; }
    public SelectList? ActionSelectList { get; set; }

    public class EntryView
    {
        public DateTime TimestampLocal { get; set; }
        public string ActionType { get; set; } = "";
        public string? BreakReason { get; set; }
        public Guid? SessionId { get; set; }
    }

    public class UserGroup
    {
        public string UserId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public List<EntryView> Entries { get; set; } = new();
    }

    public class DayGroup
    {
        public DateTime Date { get; set; }
        public List<UserGroup> Users { get; set; } = new();
    }

    public List<DayGroup> Days { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        // default to last 7 days
        var today = DateTime.UtcNow.Date;
        if (!ToDate.HasValue) ToDate = today;
        if (!FromDate.HasValue) FromDate = ToDate.Value.AddDays(-6);

        var fromUtc = DateTime.SpecifyKind(FromDate.Value.Date, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1), DateTimeKind.Utc);

        var entries = await _db.TimeTrackingEntries
            .AsNoTracking()
            .Where(e => e.TimestampUtc >= fromUtc && e.TimestampUtc < toUtc)
            .Where(e => string.IsNullOrEmpty(SelectedUserId) || e.UserId == SelectedUserId)
            .Where(e => string.IsNullOrEmpty(SelectedAction) || e.ActionType == SelectedAction)
            .OrderBy(e => e.TimestampUtc)
            .ToListAsync();

        // preload related data
        // Build full user select list (all active users)
        var allUsers = await _db.Users.AsNoTracking().OrderBy(u => u.FirstName).ThenBy(u => u.LastName).ToListAsync();
        var userItems = new List<SelectListItem> { new SelectListItem("— All —", "") };
        foreach (var u in allUsers)
        {
            var display = (u.FirstName + " " + u.LastName).Trim();
            if (string.IsNullOrWhiteSpace(display)) display = u.UserName ?? u.Email ?? u.Id;
            userItems.Add(new SelectListItem(display, u.Id));
        }
        UserSelectList = new SelectList(userItems, "Value", "Text", SelectedUserId);

        var userIds = entries.Select(e => e.UserId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var users = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim().Length > 0 ? (u.FirstName + " " + u.LastName).Trim() : (u.UserName ?? u.Email ?? u.Id));

        var breakReasonIds = entries.Select(e => e.BreakReasonId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var reasons = await _db.BreakReasons.AsNoTracking().Where(r => breakReasonIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name);

        // Actions select list
        var actions = new List<SelectListItem> {
            new SelectListItem("— All —", ""),
            new SelectListItem("Clock in", "CLOCK_IN"),
            new SelectListItem("Clock out", "CLOCK_OUT"),
            new SelectListItem("Break start", "BREAK_START"),
            new SelectListItem("Break end", "BREAK_END")
        };
        ActionSelectList = new SelectList(actions, "Value", "Text", SelectedAction);

        var groupedByDate = entries
            .GroupBy(e => e.TimestampUtc.ToLocalTime().Date)
            .OrderByDescending(g => g.Key);

        foreach (var day in groupedByDate)
        {
            var dg = new DayGroup { Date = day.Key };
            var byUser = day.GroupBy(e => e.UserId);
            foreach (var ugrp in byUser)
            {
                var ug = new UserGroup { UserId = ugrp.Key };
                ug.DisplayName = users.TryGetValue(ugrp.Key, out var dn) ? dn : ugrp.Key ?? "(unknown)";
                foreach (var e in ugrp.OrderBy(e => e.TimestampUtc))
                {
                    ug.Entries.Add(new EntryView
                    {
                        TimestampLocal = e.TimestampUtc.ToLocalTime(),
                        ActionType = e.ActionType,
                        BreakReason = e.BreakReasonId.HasValue && reasons.TryGetValue(e.BreakReasonId.Value, out var rn) ? rn : null,
                        SessionId = e.SessionId
                    });
                }
                dg.Users.Add(ug);
            }
            // order users by display name
            dg.Users = dg.Users.OrderBy(u => u.DisplayName).ToList();
            Days.Add(dg);
        }

        return Page();
    }
}
