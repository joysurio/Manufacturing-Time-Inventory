using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Models.TimeTracking;

namespace ManufacturingTimeTracking.Pages.TimeTracking;

[Authorize(Roles = "Admin")]
public class EditRecordsModel : PageModel
{
    public const string ActionClockIn = "CLOCK_IN";
    public const string ActionClockOut = "CLOCK_OUT";
    public const string ActionBreakStart = "BREAK_START";
    public const string ActionBreakEnd = "BREAK_END";

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public EditRecordsModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
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

    [BindProperty(SupportsGet = true)]
    public string? SortBy { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool SortDesc { get; set; }

    public List<EntryRow> Entries { get; set; } = new();
    public SelectList? UserSelectList { get; set; }
    /// <summary>Users only (no "All users") for Add entry dropdown.</summary>
    public SelectList? AddUserSelectList { get; set; }
    public List<BreakReason> BreakReasons { get; set; } = new();

    public class EntryRow
    {
        public int Id { get; set; }
        public string UserId { get; set; } = "";
        public string UserDisplayName { get; set; } = "";
        public string ActionType { get; set; } = "";
        public DateTime TimestampUtc { get; set; }
        public string TimestampLocal => TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        /// <summary>For datetime-local input: yyyy-MM-ddTHH:mm</summary>
        public string TimestampLocalForInput => TimestampUtc.ToLocalTime().ToString("yyyy-MM-ddTHH:mm");
        public int? BreakReasonId { get; set; }
        public string? BreakReasonName { get; set; }
        public Guid? SessionId { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadFiltersAsync();
        await LoadEntriesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostEditAsync(int id, string timestampLocal, int? breakReasonId)
    {
        var entry = await _context.TimeTrackingEntries.FindAsync(id);
        if (entry == null)
        {
            TempData["EditRecordsError"] = "Entry not found.";
            return RedirectToPage();
        }

        if (DateTime.TryParse(timestampLocal, out var localDt))
        {
            var utc = TimeZoneInfo.ConvertTimeToUtc(localDt, TimeZoneInfo.Local);
            entry.TimestampUtc = utc;
        }

        if (entry.ActionType == ActionBreakStart && breakReasonId.HasValue)
        {
            var exists = await _context.BreakReasons.AnyAsync(r => r.Id == breakReasonId.Value);
            if (exists)
                entry.BreakReasonId = breakReasonId.Value;
        }

        await _context.SaveChangesAsync();
        TempData["EditRecordsSuccess"] = "Record updated. Reports will show the new times.";
        return RedirectToPage(new { FromDate = FromDate?.ToString("yyyy-MM-dd"), ToDate = ToDate?.ToString("yyyy-MM-dd"), UserIdFilter });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var entry = await _context.TimeTrackingEntries.FindAsync(id);
        if (entry == null)
        {
            TempData["EditRecordsError"] = "Entry not found.";
            return RedirectToPage();
        }
        _context.TimeTrackingEntries.Remove(entry);
        await _context.SaveChangesAsync();
        TempData["EditRecordsSuccess"] = "Record deleted. Reports will recalculate.";
        return RedirectToPage(new { FromDate = FromDate?.ToString("yyyy-MM-dd"), ToDate = ToDate?.ToString("yyyy-MM-dd"), UserIdFilter });
    }

    public async Task<IActionResult> OnPostAddAsync(string userId, string actionType, string timestampLocal, int? breakReasonId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            TempData["EditRecordsError"] = "Select a user.";
            return RedirectToPage();
        }
        var actionTypes = new[] { ActionClockIn, ActionClockOut, ActionBreakStart, ActionBreakEnd };
        if (string.IsNullOrWhiteSpace(actionType) || !actionTypes.Contains(actionType))
        {
            TempData["EditRecordsError"] = "Select a valid action type.";
            return RedirectToPage();
        }
        if (!DateTime.TryParse(timestampLocal, out var localDt))
        {
            TempData["EditRecordsError"] = "Invalid date and time.";
            return RedirectToPage();
        }
        if (actionType == ActionBreakStart && (!breakReasonId.HasValue || breakReasonId.Value <= 0))
        {
            TempData["EditRecordsError"] = "Break start requires a break reason.";
            return RedirectToPage();
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(localDt, TimeZoneInfo.Local);
        Guid? sessionId = null;
        if (actionType == ActionClockIn)
            sessionId = Guid.NewGuid();
        else
        {
            var last = await _context.TimeTrackingEntries
                .Where(e => e.UserId == userId && e.TimestampUtc <= utc)
                .OrderByDescending(e => e.TimestampUtc)
                .FirstOrDefaultAsync();
            if (last != null)
                sessionId = last.SessionId;
        }

        _context.TimeTrackingEntries.Add(new TimeTrackingEntry
        {
            UserId = userId,
            ActionType = actionType,
            TimestampUtc = utc,
            BreakReasonId = actionType == ActionBreakStart ? breakReasonId : null,
            SessionId = sessionId
        });
        await _context.SaveChangesAsync();
        TempData["EditRecordsSuccess"] = "Entry added. Reports will recalculate.";
        return RedirectToPage(new { FromDate = FromDate?.ToString("yyyy-MM-dd"), ToDate = ToDate?.ToString("yyyy-MM-dd"), UserIdFilter });
    }

    private async Task LoadFiltersAsync()
    {
        var users = await _userManager.Users.OrderBy(u => u.Email).ToListAsync();
        var items = new List<SelectListItem> { new("All users", "") };
        var addItems = new List<SelectListItem>();
        foreach (var u in users)
        {
            var name = !string.IsNullOrWhiteSpace(u.FirstName) || !string.IsNullOrWhiteSpace(u.LastName)
                ? $"{u.FirstName} {u.LastName}".Trim()
                : u.UserName ?? u.Email ?? u.Id;
            var text = $"{name} ({u.Email})";
            items.Add(new SelectListItem(text, u.Id));
            addItems.Add(new SelectListItem(text, u.Id));
        }
        UserSelectList = new SelectList(items, "Value", "Text", UserIdFilter);
        AddUserSelectList = new SelectList(addItems, "Value", "Text");

        BreakReasons = await _context.BreakReasons
            .Where(r => r.Active)
            .OrderBy(r => r.Name)
            .ToListAsync();
    }

    private async Task LoadEntriesAsync()
    {
        var from = (FromDate ?? DateTime.UtcNow.Date.AddDays(-7)).Date;
        var to = (ToDate ?? DateTime.UtcNow.Date).Date;
        if (to < from) to = from;
        var rangeStart = from;
        var rangeEnd = to.AddDays(1);

        var query = _context.TimeTrackingEntries
            .AsNoTracking()
            .Where(e => e.TimestampUtc >= rangeStart && e.TimestampUtc < rangeEnd);

        if (!string.IsNullOrWhiteSpace(UserIdFilter))
            query = query.Where(e => e.UserId == UserIdFilter);

        // Apply sorting based on column
        query = (SortBy, SortDesc) switch
        {
            ("User", false) => query.OrderBy(e => e.UserId).ThenBy(e => e.TimestampUtc),
            ("User", true)  => query.OrderByDescending(e => e.UserId).ThenByDescending(e => e.TimestampUtc),
            ("Action", false) => query.OrderBy(e => e.ActionType).ThenBy(e => e.TimestampUtc),
            ("Action", true)  => query.OrderByDescending(e => e.ActionType).ThenByDescending(e => e.TimestampUtc),
            ("DateTime", false) => query.OrderBy(e => e.TimestampUtc),
            ("DateTime", true)  => query.OrderByDescending(e => e.TimestampUtc),
            ("BreakReason", false) => query.OrderBy(e => e.BreakReasonId).ThenBy(e => e.TimestampUtc),
            ("BreakReason", true)  => query.OrderByDescending(e => e.BreakReasonId).ThenByDescending(e => e.TimestampUtc),
            _ => query.OrderBy(e => e.UserId).ThenBy(e => e.TimestampUtc)
        };

        var list = await query
            .Include(e => e.BreakReason)
            .Take(500)
            .ToListAsync();

        var userIds = list.Select(e => e.UserId).Distinct().ToList();
        var names = new Dictionary<string, string>();
        foreach (var uid in userIds)
        {
            var u = await _userManager.FindByIdAsync(uid);
            names[uid] = u != null
                ? (!string.IsNullOrWhiteSpace(u.FirstName) || !string.IsNullOrWhiteSpace(u.LastName) ? $"{u.FirstName} {u.LastName}".Trim() : u.UserName ?? u.Email ?? uid)
                : uid;
        }

        Entries = list.Select(e => new EntryRow
        {
            Id = e.Id,
            UserId = e.UserId,
            UserDisplayName = names.GetValueOrDefault(e.UserId, e.UserId),
            ActionType = e.ActionType,
            TimestampUtc = e.TimestampUtc,
            BreakReasonId = e.BreakReasonId,
            BreakReasonName = e.BreakReason?.Name,
            SessionId = e.SessionId
        }).ToList();
    }
}
