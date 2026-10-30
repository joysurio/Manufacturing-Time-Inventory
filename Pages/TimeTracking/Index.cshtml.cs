using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Models.TimeTracking;
using ManufacturingTimeTracking.Services;
using System.Text.Json;
using System.Net.Http;

namespace ManufacturingTimeTracking.Pages.TimeTracking;

[Authorize]
public class IndexModel : PageModel
{
    public const string ActionClockIn = "CLOCK_IN";
    public const string ActionClockOut = "CLOCK_OUT";
    public const string ActionBreakStart = "BREAK_START";
    public const string ActionBreakEnd = "BREAK_END";

    private readonly ApplicationDbContext _context;
    private readonly ILogger<IndexModel>? _logger;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;

    public IndexModel(
        ApplicationDbContext context,
        IConfiguration configuration,
        IMemoryCache memoryCache,
        ILogger<IndexModel>? logger = null)
    {
        _context = context;
        _configuration = configuration;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public string UserDisplayName { get; set; } = "";
    public DateTime DateToday { get; set; }
    public string Status { get; set; } = "Not Clocked In";
    public DateTime? SessionStartedAtUtc { get; set; }
    public DateTime? BreakStartedAtUtc { get; set; }
    public TimeSpan? SessionDuration { get; set; }
    public TimeSpan? BreakDuration { get; set; }
    public TimeSpan TodayWorked { get; set; }
    public TimeSpan TodayBreak { get; set; }
    public Guid? CurrentSessionId { get; set; }
    public List<BreakReason> BreakReasons { get; set; } = new();
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }

    [BindProperty(SupportsGet = true)] public DateTime? FromDate { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? ToDate { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            ErrorMessage = "User not identified.";
            return Page();
        }

        UserDisplayName = User.Identity?.Name ?? userId;

        // Default date range: Monday of current week .. today
        var today = DateTime.UtcNow.Date;
        var daysFromMonday = ((int)today.DayOfWeek + 6) % 7; // Monday=0
        var thisMonday = today.AddDays(-daysFromMonday);
        if (!FromDate.HasValue) FromDate = thisMonday;
        if (!ToDate.HasValue) ToDate = today;

        DateToday = ToDate.Value.Date;

        // Load a small set of recent entries for status detection (keep queries fast)
        var recentEntries = await _context.TimeTrackingEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.TimestampUtc)
            .Take(50)
            .ToListAsync();

        // Load only entries in the requested date range for the page content
        var from = FromDate.Value.Date;
        var toExclusive = ToDate.Value.Date.AddDays(1);
        var entries = await _context.TimeTrackingEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.TimestampUtc >= from && e.TimestampUtc < toExclusive)
            .OrderByDescending(e => e.TimestampUtc)
            .ToListAsync();

        BreakReasons = await _context.BreakReasons
            .AsNoTracking()
            .Where(r => r.Active)
            .OrderBy(r => r.Name)
            .ToListAsync();

        var last = recentEntries.FirstOrDefault();
        if (last == null)
        {
            Status = "Not Clocked In";
        }
        else if (last.ActionType == ActionClockOut)
        {
            Status = "Not Clocked In";
        }
        else if (last.ActionType == ActionBreakStart)
        {
            Status = "On Break";
            BreakStartedAtUtc = last.TimestampUtc;
            var clockIn = recentEntries.FirstOrDefault(e => e.ActionType == ActionClockIn && e.SessionId == last.SessionId);
            if (clockIn == null)
            {
                // fallback: try to fetch clock-in for this session explicitly
                clockIn = await _context.TimeTrackingEntries.AsNoTracking()
                    .Where(e => e.UserId == userId && e.ActionType == ActionClockIn && e.SessionId == last.SessionId)
                    .OrderBy(e => e.TimestampUtc)
                    .FirstOrDefaultAsync();
            }
            if (clockIn != null)
            {
                SessionStartedAtUtc = clockIn.TimestampUtc;
                CurrentSessionId = clockIn.SessionId;
            }
            BreakDuration = DateTime.UtcNow - BreakStartedAtUtc;
        }
        else if (last.ActionType == ActionClockIn || last.ActionType == ActionBreakEnd)
        {
            Status = "Working";
            CurrentSessionId = last.SessionId;
            var clockIn = recentEntries.FirstOrDefault(e => e.ActionType == ActionClockIn && e.SessionId == last.SessionId);
            if (clockIn == null)
            {
                clockIn = await _context.TimeTrackingEntries.AsNoTracking()
                    .Where(e => e.UserId == userId && e.ActionType == ActionClockIn && e.SessionId == last.SessionId)
                    .OrderBy(e => e.TimestampUtc)
                    .FirstOrDefaultAsync();
            }
            if (clockIn != null)
                SessionStartedAtUtc = clockIn.TimestampUtc;
            if (SessionStartedAtUtc.HasValue)
                SessionDuration = DateTime.UtcNow - SessionStartedAtUtc.Value;
        }

        ComputeTodaySummary(userId, DateToday);
        await ApplyExternalWorkedTimeFallbackAsync(userId, DateToday);
        return Page();
    }

    private void ComputeTodaySummary(string userId, DateTime dateStart)
    {
        var dateEnd = dateStart.AddDays(1);
        var todayEntries = _context.TimeTrackingEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.TimestampUtc >= dateStart && e.TimestampUtc < dateEnd)
            .OrderBy(e => e.TimestampUtc)
            .ToList();

        var worked = TimeSpan.Zero;
        var breakTime = TimeSpan.Zero;
        DateTime? clockInTime = null;
        DateTime? breakStartTime = null;

        foreach (var e in todayEntries)
        {
            if (e.ActionType == ActionClockIn)
            {
                clockInTime = e.TimestampUtc;
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
        if (clockInTime.HasValue && Status == "Working")
            worked += DateTime.UtcNow - clockInTime.Value;
        else if (breakStartTime.HasValue && Status == "On Break")
            breakTime += DateTime.UtcNow - breakStartTime.Value;

        TodayWorked = worked;
        TodayBreak = breakTime;
    }

    private async Task ApplyExternalWorkedTimeFallbackAsync(string userId, DateTime dateStart, int localWorkedMinutes = -1)
    {
        try
        {
            var timeline = await TryGetExternalTimelineMetricsAsync(dateStart, userId);
            if (timeline is null)
                return;

            var mtiWorkedMinutes = localWorkedMinutes >= 0 ? localWorkedMinutes : Math.Max(0, (int)Math.Round(TodayWorked.TotalMinutes));
            if (timeline.WorkedMinutes.HasValue && timeline.WorkedMinutes.Value >= 0)
            {
                // If either source is missing/zero, use the other one. If both have data, keep the highest
                // to avoid false zeros while preventing under-reporting.
                var mergedWorkedMinutes = Math.Max(mtiWorkedMinutes, timeline.WorkedMinutes.Value);
                TodayWorked = TimeSpan.FromMinutes(mergedWorkedMinutes);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Unable to apply external worked-time fallback for user {UserId}", userId);
        }
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> that bypasses TLS certificate validation when the
    /// production config points to an IP address whose certificate belongs to a different hostname
    /// (indicated by <c>ExternalApi:HostHeader</c> being set or
    /// <c>ExternalApi:DangerousAcceptAnyServerCertificate = true</c>).
    /// </summary>
    private HttpClient CreateExternalApiHttpClient()
    {
        var dangerousCert = _configuration.GetValue<bool>("ExternalApi:DangerousAcceptAnyServerCertificate");
        var hostHeader    = _configuration["ExternalApi:HostHeader"];
        // Bypass SSL when explicitly configured OR when HostHeader is set (IP + domain-cert pattern).
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

    private async Task<TimelineMetrics?> TryGetExternalTimelineMetricsAsync(DateTime dateStart, string userIdFallback)
    {
        var baseUrl = _configuration["ExternalApi:BaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        var apiKey = _configuration["ExternalApi:ApiKey"];
        using var client = CreateExternalApiHttpClient();
        if (!string.IsNullOrEmpty(apiKey))
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        TimelineMetrics? best = null;
        foreach (var externalUser in GetExternalUserCandidates(userIdFallback))
        {
            var requestUrl = $"{baseUrl}/api/TecnicosApi/Timeline?user={Uri.EscapeDataString(externalUser)}&date={dateStart:yyyy-MM-dd}";
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                var hostHeader = _configuration["ExternalApi:HostHeader"];
                if (!string.IsNullOrEmpty(hostHeader))
                    req.Headers.Host = hostHeader;

                using var response = await client.SendAsync(req);
                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("External Timeline API returned {Status} for user {User}", response.StatusCode, externalUser);
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync();
                var parsed = ParseTimelineMetrics(json);
                if (parsed is null)
                    continue;

                // Keep first valid payload as fallback, but prefer one with non-zero data.
                best ??= parsed;
                if ((parsed.TaskMinutes ?? 0) > 0 || (parsed.WorkedMinutes ?? 0) > 0)
                    return parsed;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "External Timeline API call failed for user {User}", externalUser);
            }
        }

        return best;
    }

    private static IEnumerable<string> GetExternalUserCandidatesForProfile(string? email, string? userName)
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

    private async Task<TimelineMetrics?> TryGetExternalTimelineMetricsForProfileAsync(
        DateTime dateStart,
        string? email,
        string? userName)
    {
        var baseUrl = _configuration["ExternalApi:BaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        var apiKey = _configuration["ExternalApi:ApiKey"];
        using var client = CreateExternalApiHttpClient();
        if (!string.IsNullOrEmpty(apiKey))
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        TimelineMetrics? best = null;
        foreach (var externalUser in GetExternalUserCandidatesForProfile(email, userName))
        {
            var requestUrl = $"{baseUrl}/api/TecnicosApi/Timeline?user={Uri.EscapeDataString(externalUser)}&date={dateStart:yyyy-MM-dd}";
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                var hostHeader = _configuration["ExternalApi:HostHeader"];
                if (!string.IsNullOrEmpty(hostHeader))
                    req.Headers.Host = hostHeader;

                using var response = await client.SendAsync(req);
                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("External Timeline API returned {Status} for user {User}", response.StatusCode, externalUser);
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync();
                var parsed = ParseTimelineMetrics(json);
                if (parsed is null)
                    continue;

                best ??= parsed;
                if ((parsed.TaskMinutes ?? 0) > 0 || (parsed.WorkedMinutes ?? 0) > 0)
                    return parsed;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "External Timeline API call failed for user {User}", externalUser);
            }
        }

        return best;
    }

    private static TimelineMetrics? ParseTimelineMetrics(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var root = doc.RootElement;
            int? TryReadInt(params string[] names)
            {
                foreach (var name in names)
                {
                    if (TryGetPropertyIgnoreCase(root, name, out var prop) &&
                        prop.ValueKind == JsonValueKind.Number &&
                        prop.TryGetInt32(out var value))
                    {
                        return value;
                    }
                }
                return null;
            }

            // Supports both MTI/Websat naming variants.
            var taskMinutes = TryReadInt("taskMinutes", "tasksMinutes", "minutesTasks", "tareasMinutos");
            var workedMinutes = TryReadInt(
                "workedMinutes",
                "workMinutes",
                "factoryMinutes",
                "minutesInFactory",
                "timeInFactoryMinutes",
                "tiempoTrabajadoMinutos",
                "minutosTrabajados");

            // Some external payloads return task detail rows in "timeline" instead of a pre-aggregated taskMinutes.
            // In that case, compute task minutes by summing each timeline item's duration.
            if (!taskMinutes.HasValue &&
                TryGetPropertyIgnoreCase(root, "timeline", out var timeline) &&
                timeline.ValueKind == JsonValueKind.Array)
            {
                var totalTimelineMinutes = 0;
                var ranges = new List<(DateTimeOffset Start, DateTimeOffset End)>();
                foreach (var item in timeline.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    if (TryReadDurationMinutes(item, out var minutesFromDuration))
                    {
                        totalTimelineMinutes += Math.Max(0, minutesFromDuration);
                        continue;
                    }

                    var startOk = TryReadDateTime(item, out var start, "start", "startUtc", "from", "inicio");
                    var endOk = TryReadDateTime(item, out var end, "end", "endUtc", "to", "fin");
                    if (startOk && endOk && end > start)
                    {
                        ranges.Add((start, end));
                    }
                }

                taskMinutes = totalTimelineMinutes + CalculateMergedTimelineMinutes(ranges);
            }

            return new TimelineMetrics(taskMinutes, workedMinutes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadDateTime(JsonElement element, out DateTimeOffset value, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetPropertyIgnoreCase(element, name, out var prop))
                continue;

            if (prop.ValueKind == JsonValueKind.String &&
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

    private static bool TryReadDurationMinutes(JsonElement element, out int minutes)
    {
        minutes = 0;
        if (!TryGetPropertyIgnoreCase(element, "durationMinutes", out var durationProp))
            return false;

        if (durationProp.ValueKind == JsonValueKind.Number && durationProp.TryGetInt32(out var value))
        {
            minutes = value;
            return true;
        }

        if (durationProp.ValueKind == JsonValueKind.String &&
            int.TryParse(durationProp.GetString(), out var fromString))
        {
            minutes = fromString;
            return true;
        }

        return false;
    }

    private IEnumerable<string> GetExternalUserCandidates(string userIdFallback)
    {
        var values = new List<string>();

        var identityName = User.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(identityName))
            values.Add(identityName.Trim());

        var userName = User.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrWhiteSpace(userName))
            values.Add(userName.Trim());

        var email = User.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrWhiteSpace(email))
            values.Add(email.Trim());

        if (!string.IsNullOrWhiteSpace(userIdFallback))
            values.Add(userIdFallback.Trim());

        var expanded = new List<string>();
        foreach (var value in values)
        {
            expanded.Add(value);
            var at = value.IndexOf('@');
            if (at > 0)
                expanded.Add(value[..at]);
        }

        return expanded
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var p in element.EnumerateObject())
        {
            if (string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryParseIsActiveResponse(string body, out bool isActive)
    {
        isActive = false;
        if (string.IsNullOrWhiteSpace(body))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (TryReadBoolish(root, out isActive))
                return true;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryGetPropertyIgnoreCase(root, "isActive", out var ia) && TryReadBoolish(ia, out isActive))
                    return true;
                if (TryGetPropertyIgnoreCase(root, "active", out var a) && TryReadBoolish(a, out isActive))
                    return true;
                if (TryGetPropertyIgnoreCase(root, "openTasksCount", out var cnt) &&
                    cnt.ValueKind == JsonValueKind.Number &&
                    cnt.TryGetInt32(out var openTasks))
                {
                    isActive = openTasks > 0;
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            // fall through to plain-text parsing
        }

        return TryReadBoolishText(body, out isActive);
    }

    private static bool TryReadBoolish(JsonElement element, out bool value)
    {
        value = false;
        return element.ValueKind switch
        {
            JsonValueKind.True => SetBool(true, out value),
            JsonValueKind.False => SetBool(false, out value),
            JsonValueKind.Number when element.TryGetInt32(out var n) => SetBool(n > 0, out value),
            JsonValueKind.String => TryReadBoolishText(element.GetString(), out value),
            _ => false
        };
    }

    private static bool TryReadBoolishText(string? text, out bool value)
    {
        value = false;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim().Trim('"').ToLowerInvariant();
        if (normalized is "true" or "1" or "yes" or "y" or "si" or "sí")
        {
            value = true;
            return true;
        }

        if (normalized is "false" or "0" or "no" or "n")
        {
            value = false;
            return true;
        }

        return false;
    }

    private static bool SetBool(bool source, out bool value)
    {
        value = source;
        return true;
    }

    private sealed record TimelineMetrics(int? TaskMinutes, int? WorkedMinutes);

    private async Task<int> CalculateInternalTaskMinutesAsync(string userId, DateTime dateStart, DateTime dateEnd, DateTime utcNow)
    {
        var taskMinutes = 0;

        // Same source used by ReportTimeByUser: finished/in-progress step runs.
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

        // Same source used by ReportTimeByUser: internal task sessions net of pauses.
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

    private async Task<int> CalculateMaxInternalTaskMinutesForDayAsync(DateTime dateStart, DateTime dateEnd, DateTime utcNow)
    {
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

        var allUserIds = stepUserIds.Union(taskUserIds).Distinct().ToList();
        var maxMinutes = 0;
        foreach (var uid in allUserIds)
        {
            var minutes = await CalculateInternalTaskMinutesAsync(uid, dateStart, dateEnd, utcNow);
            if (minutes > maxMinutes) maxMinutes = minutes;
        }
        return maxMinutes;
    }

    private async Task<int> GetMergedTaskMinutesForUserAsync(string userId, DateTime dateStart, DateTime dateEnd, DateTime utcNow)
    {
        var internalM = await CalculateInternalTaskMinutesAsync(userId, dateStart, dateEnd, utcNow);
        var baseUrl = _configuration["ExternalApi:BaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            return internalM;

        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return internalM;

        var ext = await TryGetExternalTimelineMetricsForProfileAsync(dateStart, user.Email, user.UserName);
        var extM = Math.Max(0, ext?.TaskMinutes ?? 0);
        return Math.Max(internalM, extM);
    }

    private async Task<int> ComputeMaxMergedTaskMinutesForDayUncachedAsync(DateTime dateStart, DateTime dateEnd, DateTime utcNow)
    {
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
            var allIds = await _context.Users.AsNoTracking().Select(u => u.Id).ToListAsync();
            foreach (var id in allIds)
                userIds.Add(id);
        }

        var maxMinutes = 0;
        foreach (var uid in userIds)
        {
            var m = await GetMergedTaskMinutesForUserAsync(uid, dateStart, dateEnd, utcNow);
            if (m > maxMinutes) maxMinutes = m;
        }

        return maxMinutes;
    }

    public async Task<IActionResult> OnPostClockInAsync(string? returnUrl)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            TempData["TimeTrackingError"] = "User not identified.";
            return RedirectToPage();
        }

        // If there are open runs or task sessions, warn the user but STILL allow clock-in.
        var openRuns = await _context.StepRuns
            .AsNoTracking()
            .Include(r => r.StepExec).ThenInclude(se => se!.PhaseExec).ThenInclude(pe => pe!.BuildExecution).ThenInclude(be => be!.BuildOrder)
            .Include(r => r.Workstation)
            .Where(r => r.UserId == userId && r.FinishedAt == null)
            .ToListAsync();

        var openTaskSessions = await _context.TaskSessions
            .AsNoTracking()
            .Include(s => s.Task)
            .Where(s => s.UserId == userId && s.EndedAtUtc == null)
            .ToListAsync();

        if (openRuns.Any() || openTaskSessions.Any())
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<div style='padding:16px;border-radius:12px;background:#fffaf0;border:2px solid #f59e0b;color:#7c2d12;'>");
            sb.AppendLine("<h3 style='margin:0 0 6px 0;font-size:18px;'>Advertencia: tienes tareas sin finalizar</h3>");
            sb.AppendLine("<p style='margin:0 0 8px 0;'>Puedes fichar (inicio) ahora, pero recuerda cerrar tus tareas activas. Detalles:</p>");
            sb.AppendLine("<ul style='margin:0;padding-left:18px;'>");
            foreach (var run in openRuns)
            {
                var stepTitle = run.StepExec?.Title ?? "(sin t�tulo)";
                var order = run.StepExec?.PhaseExec?.BuildExecution?.BuildOrder;
                var machine = order != null ? (string.IsNullOrEmpty(order.SerialNumber) ? order.ExternalRef : $"{order.ExternalRef} / {order.SerialNumber}") : "(orden desconocida)";
                var ws = run.Workstation?.Name ?? (run.WorkstationId.HasValue ? ("WS " + run.WorkstationId.Value) : "(puesto desconocido)");
                var stepLink = $"/Orders/StepView?stepExecId={run.StepExecId}";
                sb.AppendLine($"<li style='margin-bottom:6px;'><strong>{System.Net.WebUtility.HtmlEncode(stepTitle)}</strong> � M�quina: {System.Net.WebUtility.HtmlEncode(machine)} � Puesto: {System.Net.WebUtility.HtmlEncode(ws)} &nbsp; <a href=\"{System.Net.WebUtility.HtmlEncode(stepLink)}\" target=\"_blank\" style=\"font-weight:600;color:#b91c1c;\">Abrir</a></li>");
            }
            foreach (var ts in openTaskSessions)
            {
                var taskTitle = ts.Task?.Title ?? "(tarea interna)";
                var taskId = ts.Task?.Id;
                var taskLink = taskId.HasValue ? $"/Tasks/Detail?id={taskId.Value}" : "#";
                if (taskId.HasValue)
                    sb.AppendLine($"<li style='margin-bottom:6px;'><strong>{System.Net.WebUtility.HtmlEncode(taskTitle)}</strong> � Tarea interna pendiente &nbsp; <a href=\"{System.Net.WebUtility.HtmlEncode(taskLink)}\" target=\"_blank\" style=\"font-weight:600;color:#b91c1c;\">Abrir</a></li>");
                else
                    sb.AppendLine($"<li style='margin-bottom:6px;'><strong>{System.Net.WebUtility.HtmlEncode(taskTitle)}</strong> � Tarea interna pendiente</li>");
            }
            sb.AppendLine("</ul>");
            sb.AppendLine("</div>");
            TempData["TimeTrackingOpenTasksWarning"] = sb.ToString();
        }

        var sessionId = Guid.NewGuid();
        _context.TimeTrackingEntries.Add(new TimeTrackingEntry
        {
            UserId = userId,
            ActionType = ActionClockIn,
            TimestampUtc = DateTime.UtcNow,
            SessionId = sessionId
        });
        await _context.SaveChangesAsync();
        TempData["TimeTrackingSuccess"] = "Clocked in.";

        // If the user's local time is after 07:00, show a prominent late warning
        try
        {
            var localNow = DateTime.UtcNow.ToLocalTime();
            var cutoff = TimeSpan.FromHours(7);
            if (localNow.TimeOfDay > cutoff)
            {
                var lateMsg = new System.Text.StringBuilder();
                lateMsg.AppendLine("<div style='padding:20px;border-radius:12px;background:#fff9eb;border:2px solid #f59e0b;color:#7c2d12;'>");
                lateMsg.AppendLine("<h2 style='margin:0 0 8px 0;font-size:20px;'>Fichaje tarde</h2>");
                lateMsg.AppendLine("<p style='margin:0 0 6px 0;'>Has fichado despu�s de las 07:00. Esto contar� como fichaje tarde y se aplicar� una penalizaci�n en la puntuaci�n diaria.</p>");
                lateMsg.AppendLine("<p style='margin:0;font-weight:600;'>Hora de fichaje: " + System.Net.WebUtility.HtmlEncode(localNow.ToString("HH:mm")) + "</p>");
                lateMsg.AppendLine("</div>");
                TempData["TimeTrackingWarningLate"] = lateMsg.ToString();
            }
        }
        catch
        {
            // If timezone conversion fails for any reason, ignore and do not block clock-in
        }
        if (TryGetSafeLocalReturnUrl(returnUrl, out var safeReturn))
            return LocalRedirect(safeReturn);
        return RedirectToPage();
    }

    /// <summary>
    /// Optional relative return URL (e.g. /Tasks/Detail?id=3) after clock-in from another page.
    /// </summary>
    private static bool TryGetSafeLocalReturnUrl(string? returnUrl, out string pathAndQuery)
    {
        pathAndQuery = "";
        if (string.IsNullOrWhiteSpace(returnUrl)) return false;
        var u = returnUrl.Trim();
        if (!u.StartsWith('/')) return false;
        if (u.StartsWith("//", StringComparison.Ordinal)) return false;
        if (u.Contains("://", StringComparison.Ordinal)) return false;
        pathAndQuery = u;
        return true;
    }

    public async Task<IActionResult> OnPostClockOutAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return RedirectToPage();

        // Before allowing clock out, ensure user has no open step runs or task sessions
        var openRuns = await _context.StepRuns
            .AsNoTracking()
            .Include(r => r.StepExec).ThenInclude(se => se!.PhaseExec).ThenInclude(pe => pe!.BuildExecution).ThenInclude(be => be!.BuildOrder)
            .Include(r => r.Workstation)
            .Where(r => r.UserId == userId && r.FinishedAt == null)
            .ToListAsync();

        var openTaskSessions = await _context.TaskSessions
            .AsNoTracking()
            .Include(s => s.Task)
            .Where(s => s.UserId == userId && s.EndedAtUtc == null)
            .ToListAsync();

        if (openRuns.Any() || openTaskSessions.Any())
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<div style='padding:20px;border-radius:12px;background:#fff6f6;border:2px solid #fca5a5;color:#7f1d1d;'>");
            sb.AppendLine("<h2 style='margin-top:0;margin-bottom:8px;font-size:20px;'>No puedes fichar salida: tienes tareas sin finalizar</h2>");
            sb.AppendLine("<p style='margin:0 0 8px 0;'>Cierra las tareas activas antes de fichar la salida. Detalles:</p>");
            sb.AppendLine("<ul style='margin:0;padding-left:18px;'>");

            foreach (var run in openRuns)
            {
                var stepTitle = run.StepExec?.Title ?? "(sin t�tulo)";
                var order = run.StepExec?.PhaseExec?.BuildExecution?.BuildOrder;
                var machine = order != null ? (string.IsNullOrEmpty(order.SerialNumber) ? order.ExternalRef : $"{order.ExternalRef} / {order.SerialNumber}") : "(orden desconocida)";
                var ws = run.Workstation?.Name ?? (run.WorkstationId.HasValue ? ("WS " + run.WorkstationId.Value) : "(puesto desconocido)");
                var stepLink = $"/Orders/StepView?stepExecId={run.StepExecId}";
                sb.AppendLine($"<li style='margin-bottom:6px;'><strong>{System.Net.WebUtility.HtmlEncode(stepTitle)}</strong> � M�quina: {System.Net.WebUtility.HtmlEncode(machine)} � Puesto: {System.Net.WebUtility.HtmlEncode(ws)} &nbsp; <a href=\"{System.Net.WebUtility.HtmlEncode(stepLink)}\" target=\"_blank\" style=\"font-weight:600;color:#b91c1c;\">Abrir</a></li>");
            }

            foreach (var ts in openTaskSessions)
            {
                var taskTitle = ts.Task?.Title ?? "(tarea interna)";
                var taskId = ts.Task?.Id;
                var taskLink = taskId.HasValue ? $"/Tasks/Detail?id={taskId.Value}" : "#";
                if (taskId.HasValue)
                    sb.AppendLine($"<li style='margin-bottom:6px;'><strong>{System.Net.WebUtility.HtmlEncode(taskTitle)}</strong> � Tarea interna pendiente &nbsp; <a href=\"{System.Net.WebUtility.HtmlEncode(taskLink)}\" target=\"_blank\" style=\"font-weight:600;color:#b91c1c;\">Abrir</a></li>");
                else
                    sb.AppendLine($"<li style='margin-bottom:6px;'><strong>{System.Net.WebUtility.HtmlEncode(taskTitle)}</strong> � Tarea interna pendiente</li>");
            }

            sb.AppendLine("</ul>");
            sb.AppendLine("</div>");

            TempData["TimeTrackingBlock"] = sb.ToString();
            return RedirectToPage();
        }

        // Check external system (websat) for open tasks before allowing clock out
        try
        {
            var baseUrl = _configuration["ExternalApi:BaseUrl"]?.TrimEnd('/');
            var apiKey = _configuration["ExternalApi:ApiKey"];
            if (!string.IsNullOrEmpty(baseUrl))
            {
                var apiUrl = baseUrl + "/api/TecnicosApi/IsActive"; // use IsActive endpoint
                using var client = CreateExternalApiHttpClient();
                if (!string.IsNullOrEmpty(apiKey))
                    client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
                client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                var currentUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                var candidates = GetExternalUserCandidatesForProfile(currentUser?.Email, currentUser?.UserName)
                    .Concat(GetExternalUserCandidates(userId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var externalUser in candidates)
                {
                    var requestUrl = $"{apiUrl}?user={System.Uri.EscapeDataString(externalUser)}";
                    HttpResponseMessage externalResponse = null;
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                        var hostHeader = _configuration["ExternalApi:HostHeader"]; // optional override
                        if (!string.IsNullOrEmpty(hostHeader))
                        {
                            req.Headers.Host = hostHeader;
                            _logger?.LogInformation("Setting Host header to {host} for external IsActive request", hostHeader);
                        }

                        _logger?.LogInformation("External API (IsActive) request: url={url}, externalUser={user}, hasApiKey={hasKey}", requestUrl, externalUser, !string.IsNullOrEmpty(apiKey));
                        externalResponse = await client.SendAsync(req);

                        if (externalResponse != null)
                        {
                            _logger?.LogInformation("External API (IsActive) returned status {status} for user {user}", externalResponse.StatusCode, externalUser);
                            string respBody = null;
                            try { respBody = await externalResponse.Content.ReadAsStringAsync(); } catch { }
                            if (!string.IsNullOrEmpty(respBody))
                                _logger?.LogDebug("External API (IsActive) response body: {body}", respBody);
                        }
                    }
                    catch (Exception httpEx)
                    {
                        _logger?.LogWarning(httpEx, "External API request failed when checking IsActive: {url}", requestUrl);
                        externalResponse = null;
                    }

                    if (externalResponse != null && externalResponse.IsSuccessStatusCode)
                    {
                        var body = await externalResponse.Content.ReadAsStringAsync();
                        if (TryParseIsActiveResponse(body, out var isActive) && isActive)
                        {
                            TempData["TimeTrackingError"] = "No puedes fichar la salida: tienes tareas abiertas en Websat. Cierra o marca las tareas como finalizadas en la otra aplicaci�n antes de fichar salida.";
                            return RedirectToPage();
                        }
                    }
                    else if (externalResponse != null)
                    {
                        var errBody = string.Empty;
                        try { errBody = await externalResponse.Content.ReadAsStringAsync(); } catch { }
                        _logger?.LogWarning("External API returned {status} when checking IsActive for user {user}: {body}", externalResponse.StatusCode, externalUser, errBody);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error while checking external IsActive (will not block clock-out).");
        }

        var last = await _context.TimeTrackingEntries
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.TimestampUtc)
            .FirstOrDefaultAsync();

        if (last == null || last.ActionType == ActionClockOut)
        {
            TempData["TimeTrackingError"] = "You must clock in before clocking out.";
            return RedirectToPage();
        }
        if (last.ActionType == ActionBreakStart)
        {
            TempData["TimeTrackingError"] = "Please end your break before clocking out.";
            return RedirectToPage();
        }

        _context.TimeTrackingEntries.Add(new TimeTrackingEntry
        {
            UserId = userId,
            ActionType = ActionClockOut,
            TimestampUtc = DateTime.UtcNow,
            SessionId = last.SessionId
        });
        await _context.SaveChangesAsync();
        TempData["TimeTrackingSuccess"] = "Clocked out.";

        // Build end-of-day visual summary (entry time, exit time, tasks vs factory)
        try
        {
            var dateStart = DateTime.UtcNow.Date;
            var dateEnd = dateStart.AddDays(1);

            // First clock-in of the day
            var firstClockIn = _context.TimeTrackingEntries
                .AsNoTracking()
                .Where(e => e.UserId == userId && e.ActionType == ActionClockIn && e.TimestampUtc >= dateStart && e.TimestampUtc < dateEnd)
                .OrderBy(e => e.TimestampUtc)
                .FirstOrDefault();

            // Last clock-out of the day
            var lastClockOut = _context.TimeTrackingEntries
                .AsNoTracking()
                .Where(e => e.UserId == userId && e.ActionType == ActionClockOut && e.TimestampUtc >= dateStart && e.TimestampUtc < dateEnd)
                .OrderByDescending(e => e.TimestampUtc)
                .FirstOrDefault();

            // Minutes in factory: compute worked minutes using MTI source
            ComputeTodaySummary(userId, dateStart);
            var mtiWorkedMinutes = Math.Max(0, (int)TodayWorked.TotalMinutes);
            var minutesInFactory = mtiWorkedMinutes;

            // Task minutes: merge MTI internal + external API (MAX to avoid double-counting).
            var internalTaskMinutes = await CalculateInternalTaskMinutesAsync(userId, dateStart, dateEnd, DateTime.UtcNow);
            var taskMinutes = internalTaskMinutes;

            try
            {
                var externalTimeline = await TryGetExternalTimelineMetricsAsync(dateStart, userId);
                if (externalTimeline != null)
                {
                    if (externalTimeline.TaskMinutes.HasValue)
                        taskMinutes = Math.Max(taskMinutes, Math.Max(0, externalTimeline.TaskMinutes.Value));
                    if (externalTimeline.WorkedMinutes.HasValue)
                        minutesInFactory = Math.Max(mtiWorkedMinutes, Math.Max(0, externalTimeline.WorkedMinutes.Value));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error fetching external timeline metrics.");
            }

            // Percentage: task time relative to 7:40 full workday (460 min = 100%).
            const int FullDayMinutes = 7 * 60 + 40;
            double pct = Math.Min(100.0, 100.0 * taskMinutes / FullDayMinutes);

            // Scoring per criterion: 1 = green, 0.5 = orange, 0 = red
            double scoreEntry = 0, scoreExit = 0, scoreTasks = 0;
            string entryLabel = "No hay registro de entrada";
            if (firstClockIn != null)
            {
                var localEntry = firstClockIn.TimestampUtc.ToLocalTime();
                entryLabel = localEntry.ToString("HH:mm");
                if (localEntry.TimeOfDay <= TimeSpan.FromHours(7)) scoreEntry = 1;
                else scoreEntry = 0;
            }
            string exitLabel = "No hay registro de salida";
            if (lastClockOut != null)
            {
                var localExit = lastClockOut.TimestampUtc.ToLocalTime();
                exitLabel = localExit.ToString("HH:mm");
                if (localExit.TimeOfDay >= TimeSpan.FromHours(15)) scoreExit = 1;
                else scoreExit = 0;
            }

            if (minutesInFactory > 0)
            {
                if (pct >= 90) scoreTasks = 1;
                else if (pct >= 75) scoreTasks = 0.5;
                else scoreTasks = 0;
            }

            // Compute effective worked and break times (worked = gross time minus breaks).
            TodayWorked = TimeSpan.FromMinutes(minutesInFactory);
            var grossWorkedSeconds = (int)TodayWorked.TotalSeconds;
            var breakSeconds = (int)TodayBreak.TotalSeconds;
            var workedSeconds = Math.Max(0, grossWorkedSeconds - breakSeconds);

            const int EffectiveTargetSeconds = 7 * 3600 + 40 * 60; // 7:40
            const int BreakTargetSeconds = 20 * 60;

            double scoreWorked = 0, scoreBreak = 0;
            if (workedSeconds >= EffectiveTargetSeconds) scoreWorked = 1;
            else if (workedSeconds >= EffectiveTargetSeconds * 0.75) scoreWorked = 0.5;
            else scoreWorked = 0;

            // Break: green if <= target, orange if <= target*2, else red
            if (breakSeconds <= BreakTargetSeconds) scoreBreak = 1;
            else if (breakSeconds <= BreakTargetSeconds * 2) scoreBreak = 0.5;
            else scoreBreak = 0;

            var avg = (scoreEntry + scoreExit + scoreTasks + scoreWorked + scoreBreak) / 5.0;

            var taskSeconds = taskMinutes * 60;
            var eod = DailyEvaluationHtml.BuildEndOfDaySummaryHtml(
                scoreEntry, scoreExit, scoreTasks, scoreWorked, scoreBreak,
                avg, entryLabel, exitLabel, taskSeconds, workedSeconds, breakSeconds);
            try { _logger?.LogInformation("EndOfDay summary length: {len}, External Task Minutes: {taskMinutes}", eod?.Length ?? 0, taskMinutes); } catch { }
            TempData["EndOfDaySummaryHtml"] = eod;

            var evalRow = await _context.DailyTimeEvaluations
                .FirstOrDefaultAsync(x => x.UserId == userId && x.EvalDateUtc == dateStart);
            if (evalRow == null)
            {
                evalRow = new DailyTimeEvaluation { UserId = userId, EvalDateUtc = dateStart };
                _context.DailyTimeEvaluations.Add(evalRow);
            }

            evalRow.ScoreEntry = (decimal)scoreEntry;
            evalRow.ScoreExit = (decimal)scoreExit;
            evalRow.ScoreTasks = (decimal)scoreTasks;
            evalRow.ScoreWorked = (decimal)scoreWorked;
            evalRow.ScoreBreak = (decimal)scoreBreak;
            evalRow.AverageScore = (decimal)avg;
            evalRow.EntryLabel = entryLabel.Length > 200 ? entryLabel[..200] : entryLabel;
            evalRow.ExitLabel = exitLabel.Length > 200 ? exitLabel[..200] : exitLabel;
            evalRow.TaskPercent = (decimal)pct;
            evalRow.WorkedSeconds = workedSeconds;
            evalRow.BreakSeconds = breakSeconds;
            evalRow.TaskMinutes = taskMinutes;
            evalRow.MinutesInFactory = minutesInFactory;
            evalRow.SummaryHtml = eod;
            evalRow.RecordedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error generating end-of-day summary.");
            // ignore summary errors
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostStartBreakAsync(int? breakReasonId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return RedirectToPage();

        var last = await _context.TimeTrackingEntries
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.TimestampUtc)
            .FirstOrDefaultAsync();

        if (last == null || last.ActionType == ActionClockOut)
        {
            TempData["TimeTrackingError"] = "You must clock in before starting a break.";
            return RedirectToPage();
        }
        if (last.ActionType == ActionBreakStart)
        {
            TempData["TimeTrackingError"] = "You are already on a break. End it first.";
            return RedirectToPage();
        }

        if (!breakReasonId.HasValue || breakReasonId.Value <= 0)
        {
            TempData["TimeTrackingError"] = "Please select a break reason.";
            return RedirectToPage();
        }

        var reasonExists = await _context.BreakReasons.AnyAsync(r => r.Id == breakReasonId.Value && r.Active);
        if (!reasonExists)
        {
            TempData["TimeTrackingError"] = "Invalid break reason.";
            return RedirectToPage();
        }

        _context.TimeTrackingEntries.Add(new TimeTrackingEntry
        {
            UserId = userId,
            ActionType = ActionBreakStart,
            BreakReasonId = breakReasonId.Value,
            TimestampUtc = DateTime.UtcNow,
            SessionId = last.SessionId
        });
        await _context.SaveChangesAsync();
        TempData["TimeTrackingSuccess"] = "Break started.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEndBreakAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return RedirectToPage();

        var last = await _context.TimeTrackingEntries
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.TimestampUtc)
            .FirstOrDefaultAsync();

        if (last == null || last.ActionType != ActionBreakStart)
        {
            TempData["TimeTrackingError"] = "You are not on a break. Start a break first.";
            return RedirectToPage();
        }

        _context.TimeTrackingEntries.Add(new TimeTrackingEntry
        {
            UserId = userId,
            ActionType = ActionBreakEnd,
            TimestampUtc = DateTime.UtcNow,
            SessionId = last.SessionId
        });
        await _context.SaveChangesAsync();
        TempData["TimeTrackingSuccess"] = "Break ended.";

        // If the break length is > 20 minutes, show a prominent warning modal to the user
        try
        {
            var breakDuration = DateTime.UtcNow - last.TimestampUtc;
            var breakCutoff = TimeSpan.FromMinutes(20);
            if (breakDuration > breakCutoff)
            {
                var lateMsg = new System.Text.StringBuilder();
                lateMsg.AppendLine("<div style='padding:20px;border-radius:12px;background:#fff9eb;border:2px solid #f59e0b;color:#7c2d12;'>");
                lateMsg.AppendLine("<h2 style='margin:0 0 8px 0;font-size:20px;'>Descanso prolongado</h2>");
                lateMsg.AppendLine("<p style='margin:0 0 6px 0;'>Has superado los 20 minutos de descanso. Esto puede afectar a la puntuaci�n diaria.</p>");
                lateMsg.AppendLine("<p style='margin:0;font-weight:600;'>Duraci�n del descanso: " + System.Net.WebUtility.HtmlEncode(breakDuration.ToString(@"hh\:mm")) + "</p>");
                lateMsg.AppendLine("</div>");
                TempData["TimeTrackingWarningLongBreak"] = lateMsg.ToString();
            }
        }
        catch
        {
            // ignore
        }

        return RedirectToPage();
    }

    // Acknowledge long break warning (posted from modal). Redirects back to page to clear TempData.
    public IActionResult OnPostAcknowledgeLongBreak()
    {
        return RedirectToPage();
    }

    // Acknowledge late warning (posted from modal). Redirects back to page to clear TempData.
    public IActionResult OnPostAcknowledgeLate()
    {
        // Nothing to save for now. TempData is consumed when rendering the page.
        return RedirectToPage();
    }
}
