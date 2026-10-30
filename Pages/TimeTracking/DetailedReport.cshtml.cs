using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Models.TimeTracking;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ManufacturingTimeTracking.Pages.TimeTracking;

[Authorize(Policy = "CanUseTimeManagement")]
public class DetailedReportModel : PageModel
{
    private const string ActionClockIn = "CLOCK_IN";
    private const string ActionClockOut = "CLOCK_OUT";
    private const string ActionBreakStart = "BREAK_START";
    private const string ActionBreakEnd = "BREAK_END";

    private static readonly TimeSpan TheoreticalWeek = TimeSpan.FromHours(40);

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public DetailedReportModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
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

    public SelectList? UserSelectList { get; set; }

    /// <summary>URL for Export PDF button (same filter as current report).</summary>
    public string ExportPdfUrl { get; set; } = "";

    public List<UserBlock> Users { get; set; } = new();

    public class UserBlock
    {
        public string UserId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Dni { get; set; }
        public List<WeekBlock> Weeks { get; set; } = new();
    }

    public class WeekBlock
    {
        public DateTime WeekStartLocal { get; set; } // Monday
        public DateTime WeekEndLocal => WeekStartLocal.AddDays(6);
        public List<DayBlock> Days { get; set; } = new();

        public TimeSpan TotalWorked => Days.Aggregate(TimeSpan.Zero, (a, d) => a + d.TotalWorked);
        public TimeSpan TotalBreak => Days.Aggregate(TimeSpan.Zero, (a, d) => a + d.TotalBreak);
        public TimeSpan Theoretical => TheoreticalWeek;
        public TimeSpan Difference => TotalWorked - TheoreticalWeek;
    }

    public class DayBlock
    {
        public DateTime DateLocal { get; set; }
        public List<(DateTime startUtc, DateTime endUtc)> SessionsUtc { get; set; } = new();
        public List<(DateTime startUtc, DateTime endUtc)> BreaksUtc { get; set; } = new();
        public List<DateTime> ClockInUtc { get; set; } = new();
        public List<DateTime> ClockOutUtc { get; set; } = new();
        public TimeSpan TotalWorked { get; set; }
        public TimeSpan TotalBreak { get; set; }
    }

    public static string FormatHm(TimeSpan t, bool showSign = false)
    {
        var totalMinutes = (int)Math.Round(t.TotalMinutes);
        var sign = "";
        if (showSign)
        {
            sign = totalMinutes < 0 ? "-" : "+";
        }
        totalMinutes = Math.Abs(totalMinutes);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return $"{sign}{hours:00}:{minutes:00}";
    }

    public static string FormatTimeLocal(DateTime utc)
        => utc.ToLocalTime().ToString("HH:mm");

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadReportDataAsync();
        ExportPdfUrl = Url.Page("./DetailedReport", "ExportPdf", new { FromDate = FromDate?.ToString("yyyy-MM-dd"), ToDate = ToDate?.ToString("yyyy-MM-dd"), UserIdFilter }) ?? "";
        return Page();
    }

    public async Task<IActionResult> OnGetExportPdfAsync()
    {
        var (from, to) = await LoadReportDataAsync();
        if (!Users.Any())
            return RedirectToPage();

        QuestPDF.Settings.License = LicenseType.Community;
        var pdfBytes = BuildTimeTrackingPdf(Users, from, to);
        var fileName = Users.Count == 1
            ? $"TimeTracking_{Users[0].DisplayName?.Replace(" ", "_") ?? "Worker"}_{from:yyyy-MM-dd}_to_{to:yyyy-MM-dd}.pdf"
            : $"TimeTracking_Report_{from:yyyy-MM-dd}_to_{to:yyyy-MM-dd}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    /// <summary>Loads report data into Users and UserSelectList. Returns the resolved date range (from, to).</summary>
    private async Task<(DateTime from, DateTime to)> LoadReportDataAsync()
    {
        var from = (FromDate ?? DateTime.UtcNow.Date.AddDays(-14)).Date;
        var to = (ToDate ?? DateTime.UtcNow.Date).Date;
        if (to < from) to = from;

        var rangeStartUtc = from;
        var rangeEndUtcExclusive = to.AddDays(1);

        var query = _context.TimeTrackingEntries
            .AsNoTracking()
            .Where(e => e.TimestampUtc >= rangeStartUtc && e.TimestampUtc < rangeEndUtcExclusive);

        if (!string.IsNullOrWhiteSpace(UserIdFilter))
            query = query.Where(e => e.UserId == UserIdFilter);

        var entries = await query
            .OrderBy(e => e.UserId)
            .ThenBy(e => e.TimestampUtc)
            .ToListAsync();

        var effectiveNowUtc = DateTime.UtcNow < rangeEndUtcExclusive ? DateTime.UtcNow : rangeEndUtcExclusive;

        var allUserIds = await _context.TimeTrackingEntries
            .Select(e => e.UserId)
            .Distinct()
            .ToListAsync();
        var userItems = new List<SelectListItem> { new SelectListItem("All users", "") };
        foreach (var uid in allUserIds.OrderBy(x => x))
        {
            var user = await _userManager.FindByIdAsync(uid);
            userItems.Add(new SelectListItem(user?.UserName ?? user?.Email ?? uid, uid));
        }
        UserSelectList = new SelectList(userItems, "Value", "Text", UserIdFilter);

        var userIds = entries.Select(e => e.UserId).Distinct().ToList();
        var displayNames = new Dictionary<string, string>();
        var userProfiles = new Dictionary<string, (string? FirstName, string? LastName, string? Dni)>();
        foreach (var uid in userIds)
        {
            var user = await _userManager.FindByIdAsync(uid);
            displayNames[uid] = user?.UserName ?? user?.Email ?? uid;
            userProfiles[uid] = (user?.FirstName, user?.LastName, user?.Dni);
        }

        Users = new List<UserBlock>();
        foreach (var uid in userIds)
        {
            var userEntries = entries.Where(e => e.UserId == uid).OrderBy(e => e.TimestampUtc).ToList();
            var days = ComputeDays(userEntries, rangeStartUtc, rangeEndUtcExclusive, effectiveNowUtc);

            var weeks = days
                .GroupBy(d => StartOfWeekMonday(d.DateLocal))
                .OrderBy(g => g.Key)
                .Select(g => new WeekBlock
                {
                    WeekStartLocal = g.Key,
                    Days = g.OrderBy(x => x.DateLocal).ToList()
                })
                .ToList();

            var profile = userProfiles.GetValueOrDefault(uid);
            Users.Add(new UserBlock
            {
                UserId = uid,
                DisplayName = displayNames.GetValueOrDefault(uid, uid),
                FirstName = profile.FirstName,
                LastName = profile.LastName,
                Dni = profile.Dni,
                Weeks = weeks
            });
        }

        return (from, to);
    }

    private static byte[] BuildTimeTrackingPdf(List<UserBlock> users, DateTime from, DateTime to)
    {
        using var stream = new MemoryStream();
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9));
                page.Header().Column(c =>
                {
                    c.Item().Text("Detailed Time Tracking Report").Bold().FontSize(14);
                    c.Item().Text($"Period: {from:yyyy-MM-dd} to {to:yyyy-MM-dd}").FontSize(10);
                });

                page.Content().PaddingTop(8).Column(column =>
                {
                    foreach (var u in users)
                    {
                        column.Item().PaddingTop(12).Column(userCol =>
                        {
                            userCol.Item().Row(row =>
                            {
                                row.RelativeItem().Text($"Employee: {u.DisplayName}").Bold().FontSize(11);
                                var identity = new List<string>();
                                if (!string.IsNullOrWhiteSpace(u.FirstName) || !string.IsNullOrWhiteSpace(u.LastName))
                                    identity.Add($"{u.FirstName} {u.LastName}".Trim());
                                if (!string.IsNullOrWhiteSpace(u.Dni))
                                    identity.Add($"DNI: {u.Dni}");
                                if (identity.Count > 0)
                                    row.RelativeItem().Text(string.Join(" · ", identity)).FontSize(9);
                            });
                            userCol.Item().PaddingBottom(6);

                            foreach (var w in u.Weeks)
                            {
                                userCol.Item().PaddingTop(4).Text($"Week {w.WeekStartLocal:yyyy-MM-dd} → {w.WeekEndLocal:yyyy-MM-dd}")
                                    .Bold().FontSize(10);
                                userCol.Item().PaddingBottom(4).Row(r =>
                                {
                                    r.RelativeItem().Text($"Worked: {FormatHm(w.TotalWorked)}").FontSize(8);
                                    r.RelativeItem().Text($"Breaks: {FormatHm(w.TotalBreak)}").FontSize(8);
                                    r.RelativeItem().Text($"Theoretical: {FormatHm(w.Theoretical)}").FontSize(8);
                                    r.RelativeItem().Text($"Difference: {FormatHm(w.Difference, showSign: true)}").FontSize(8);
                                });

                                userCol.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(def =>
                                    {
                                        def.ConstantColumn(70);
                                        def.RelativeColumn(2);
                                        def.RelativeColumn(2);
                                        def.RelativeColumn(2);
                                        def.ConstantColumn(45);
                                        def.ConstantColumn(55);
                                    });
                                    table.Header(header =>
                                    {
                                        header.Cell().BorderBottom(1).Padding(4).Text("Date");
                                        header.Cell().BorderBottom(1).Padding(4).Text("Clock-in / Clock-out");
                                        header.Cell().BorderBottom(1).Padding(4).Text("Work sessions");
                                        header.Cell().BorderBottom(1).Padding(4).Text("Breaks");
                                        header.Cell().BorderBottom(1).Padding(4).AlignRight().Text("Total breaks");
                                        header.Cell().BorderBottom(1).Padding(4).AlignRight().Text("Total worked");
                                    });
                                    foreach (var d in w.Days)
                                    {
                                        table.Cell().Padding(4).Text(d.DateLocal.ToString("yyyy-MM-dd"));
                                        var clockText = "";
                                        if (d.ClockInUtc.Any() || d.ClockOutUtc.Any())
                                        {
                                            var lines = d.ClockInUtc.OrderBy(x => x).Select(t => $"IN {FormatTimeLocal(t)}").ToList();
                                            lines.AddRange(d.ClockOutUtc.OrderBy(x => x).Select(t => $"OUT {FormatTimeLocal(t)}"));
                                            clockText = string.Join("\n", lines);
                                        }
                                        else clockText = "—";
                                        table.Cell().Padding(4).Text(clockText);
                                        var sessionsText = d.SessionsUtc.Any()
                                            ? string.Join("\n", d.SessionsUtc.OrderBy(x => x.startUtc).Select(s => $"{FormatTimeLocal(s.startUtc)} → {FormatTimeLocal(s.endUtc)}"))
                                            : "—";
                                        table.Cell().Padding(4).Text(sessionsText);
                                        var breaksText = d.BreaksUtc.Any()
                                            ? string.Join("\n", d.BreaksUtc.OrderBy(x => x.startUtc).Select(b => $"{FormatTimeLocal(b.startUtc)} → {FormatTimeLocal(b.endUtc)}"))
                                            : "—";
                                        table.Cell().Padding(4).Text(breaksText);
                                        table.Cell().Padding(4).AlignRight().Text(FormatHm(d.TotalBreak));
                                        table.Cell().Padding(4).AlignRight().Text(FormatHm(d.TotalWorked));
                                    }
                                });
                                userCol.Item().PaddingBottom(10);
                            }
                        });
                    }
                });
            });
        }).GeneratePdf(stream);
        return stream.ToArray();
    }

    private static DateTime StartOfWeekMonday(DateTime localDate)
    {
        // localDate is date-only (midnight local time)
        var diff = (7 + (localDate.DayOfWeek - DayOfWeek.Monday)) % 7;
        return localDate.AddDays(-diff).Date;
    }

    private static List<DayBlock> ComputeDays(
        List<TimeTrackingEntry> ordered,
        DateTime rangeStartUtc,
        DateTime rangeEndUtcExclusive,
        DateTime effectiveNowUtc)
    {
        // Aggregate segments across time then split by local date.
        var dayByLocalDate = new Dictionary<DateTime, DayBlock>();

        void AddToDay(DateTime utcStart, DateTime utcEnd, bool isBreak)
        {
            if (utcEnd <= utcStart) return;

            // Split by local day boundaries so the daily view is correct
            var startLocal = utcStart.ToLocalTime();
            var endLocal = utcEnd.ToLocalTime();

            var cursorLocal = startLocal;
            while (cursorLocal < endLocal)
            {
                var dayLocal = cursorLocal.Date;
                var dayEndLocal = dayLocal.AddDays(1);
                var segEndLocal = endLocal < dayEndLocal ? endLocal : dayEndLocal;

                var segStartUtc = cursorLocal.ToUniversalTime();
                var segEndUtc = segEndLocal.ToUniversalTime();

                if (!dayByLocalDate.TryGetValue(dayLocal, out var day))
                {
                    day = new DayBlock { DateLocal = dayLocal };
                    dayByLocalDate[dayLocal] = day;
                }

                if (isBreak)
                {
                    day.BreaksUtc.Add((segStartUtc, segEndUtc));
                    day.TotalBreak += segEndUtc - segStartUtc;
                }
                else
                {
                    day.SessionsUtc.Add((segStartUtc, segEndUtc));
                    day.TotalWorked += segEndUtc - segStartUtc;
                }

                cursorLocal = segEndLocal;
            }
        }

        // Track state
        DateTime? clockInUtc = null;
        DateTime? breakStartUtc = null;

        foreach (var e in ordered)
        {
            // Ignore entries outside range (safety)
            if (e.TimestampUtc < rangeStartUtc || e.TimestampUtc >= rangeEndUtcExclusive) continue;

            var localDay = e.TimestampUtc.ToLocalTime().Date;
            if (!dayByLocalDate.TryGetValue(localDay, out var day))
            {
                day = new DayBlock { DateLocal = localDay };
                dayByLocalDate[localDay] = day;
            }

            if (e.ActionType == ActionClockIn)
            {
                day.ClockInUtc.Add(e.TimestampUtc);
                clockInUtc = e.TimestampUtc;
                breakStartUtc = null;
            }
            else if (e.ActionType == ActionClockOut)
            {
                day.ClockOutUtc.Add(e.TimestampUtc);
                if (clockInUtc.HasValue)
                {
                    // session segment
                    AddToDay(clockInUtc.Value, e.TimestampUtc, isBreak: false);
                    // if break not closed, treat it as ending at clock-out
                    if (breakStartUtc.HasValue)
                    {
                        AddToDay(breakStartUtc.Value, e.TimestampUtc, isBreak: true);
                        breakStartUtc = null;
                    }
                }
                clockInUtc = null;
            }
            else if (e.ActionType == ActionBreakStart)
            {
                breakStartUtc = e.TimestampUtc;
            }
            else if (e.ActionType == ActionBreakEnd)
            {
                if (breakStartUtc.HasValue)
                {
                    AddToDay(breakStartUtc.Value, e.TimestampUtc, isBreak: true);
                    breakStartUtc = null;
                }
            }
        }

        // In-progress session within range: add from last clock-in to effectiveNowUtc
        if (clockInUtc.HasValue && effectiveNowUtc > clockInUtc.Value)
        {
            AddToDay(clockInUtc.Value, effectiveNowUtc, isBreak: false);
            if (breakStartUtc.HasValue && effectiveNowUtc > breakStartUtc.Value)
                AddToDay(breakStartUtc.Value, effectiveNowUtc, isBreak: true);
        }

        // Convert raw worked (which currently includes breaks) into net worked by subtracting breaks.
        foreach (var day in dayByLocalDate.Values)
        {
            if (day.TotalBreak > TimeSpan.Zero)
            {
                day.TotalWorked -= day.TotalBreak;
                if (day.TotalWorked < TimeSpan.Zero)
                    day.TotalWorked = TimeSpan.Zero;
            }
        }

        // Ensure every day in range appears if user has any entry in week? Keep simple: only days with activity.
        return dayByLocalDate.Values
            .OrderBy(d => d.DateLocal)
            .ToList();
    }
}

