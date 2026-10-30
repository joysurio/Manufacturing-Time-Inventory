using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Models.Tasks;

namespace ManufacturingTimeTracking.Pages.Tasks;

[Authorize]
public class DetailModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public DetailModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public TaskItem? Task { get; set; }
    public string? AssignedUserName { get; set; }
    public TimeSpan TotalTimeSpent { get; set; }
    public TaskSession? CurrentSession { get; set; }
    public bool IsPaused { get; set; }
    public TimeSpan? CurrentSessionDuration { get; set; }
    public bool CanControlTask { get; set; }
    public List<SessionSummary> SessionSummaries { get; set; } = new();

    public class SessionSummary
    {
        public int Id { get; set; }
        public string UserName { get; set; } = "";
        public DateTime StartedAtUtc { get; set; }
        public DateTime? EndedAtUtc { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id == null) return NotFound();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        Task = await _context.Tasks
            .Include(t => t.Sessions)
            .ThenInclude(s => s.Pauses)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id.Value);

        if (Task == null) return NotFound();

        // Any user can work on a task; AssignedUserId is the "owner" but not exclusive.
        CanControlTask = true;
        var assigned = await _userManager.FindByIdAsync(Task.AssignedUserId);
        AssignedUserName = assigned != null
            ? (!string.IsNullOrWhiteSpace(assigned.FirstName) || !string.IsNullOrWhiteSpace(assigned.LastName) ? $"{assigned.FirstName} {assigned.LastName}".Trim() : assigned.UserName ?? assigned.Email)
            : Task.AssignedUserId;

        var now = DateTime.UtcNow;
        // When multiple users run the same task concurrently, show the current user's open session.
        CurrentSession = Task.Sessions.FirstOrDefault(s => s.EndedAtUtc == null && s.UserId == userId);
        if (CurrentSession != null)
        {
            var openPause = CurrentSession.Pauses.FirstOrDefault(p => p.EndUtc == null);
            IsPaused = openPause != null;
            // When paused, use pause start as effective end so the timer does not keep running
            var effectiveEnd = CurrentSession.EndedAtUtc ?? (openPause != null ? openPause.StartUtc : now);
            var sessionSpan = effectiveEnd - CurrentSession.StartedAtUtc;
            var pauseTotal = TimeSpan.Zero;
            foreach (var p in CurrentSession.Pauses)
            {
                if (p.EndUtc.HasValue && p.EndUtc.Value <= effectiveEnd)
                    pauseTotal += p.EndUtc.Value - p.StartUtc;
            }
            CurrentSessionDuration = sessionSpan - pauseTotal;
        }

        // Show total time for the current user's sessions only (CTO issue #43).
        TotalTimeSpent = TimeSpan.Zero;
        foreach (var s in Task.Sessions.Where(s => s.UserId == userId))
        {
            var openP = s.Pauses.FirstOrDefault(p => p.EndUtc == null);
            var effectiveEnd = s.EndedAtUtc ?? (openP != null ? openP.StartUtc : now);
            var span = effectiveEnd - s.StartedAtUtc;
            var pauseTotal = TimeSpan.Zero;
            foreach (var p in s.Pauses)
            {
                if (p.EndUtc.HasValue && p.EndUtc.Value <= effectiveEnd)
                    pauseTotal += p.EndUtc.Value - p.StartUtc;
            }
            TotalTimeSpent += span - pauseTotal;
        }

        var allSessions = await _context.TaskSessions
            .Where(s => s.TaskId == Task.Id)
            .OrderBy(s => s.StartedAtUtc)
            .Include(s => s.Pauses)
            .ToListAsync();
        var userIds = allSessions.Select(s => s.UserId).Distinct().ToList();
        var names = new Dictionary<string, string>();
        foreach (var uid in userIds)
        {
            var u = await _userManager.FindByIdAsync(uid);
            names[uid] = u != null ? (u.UserName ?? u.Email ?? uid) : uid;
        }
        foreach (var s in allSessions)
        {
            var openP = s.Pauses.FirstOrDefault(p => p.EndUtc == null);
            var effectiveEnd = s.EndedAtUtc ?? (openP != null ? openP.StartUtc : now);
            var span = effectiveEnd - s.StartedAtUtc;
            var pauseTotal = TimeSpan.Zero;
            foreach (var p in s.Pauses)
            {
                if (p.EndUtc.HasValue && p.EndUtc.Value <= effectiveEnd)
                    pauseTotal += p.EndUtc.Value - p.StartUtc;
            }
            SessionSummaries.Add(new SessionSummary
            {
                Id = s.Id,
                UserName = names.GetValueOrDefault(s.UserId, s.UserId),
                StartedAtUtc = s.StartedAtUtc,
                EndedAtUtc = s.EndedAtUtc,
                Duration = span - pauseTotal
            });
        }

        return Page();
    }

    private static async Task<bool> IsUserClockedInForWorkAsync(ApplicationDbContext context, string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        var entries = await context.TimeTrackingEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.TimestampUtc)
            .Take(200)
            .ToListAsync();

        var last = entries.FirstOrDefault();
        if (last == null) return false;
        if (last.ActionType == ManufacturingTimeTracking.Pages.TimeTracking.IndexModel.ActionClockOut) return false;
        // Same rules as Time Tracking page: Working or On Break counts as clocked in.
        if (last.ActionType == ManufacturingTimeTracking.Pages.TimeTracking.IndexModel.ActionBreakStart) return true;
        if (last.ActionType == ManufacturingTimeTracking.Pages.TimeTracking.IndexModel.ActionClockIn
            || last.ActionType == ManufacturingTimeTracking.Pages.TimeTracking.IndexModel.ActionBreakEnd) return true;
        return false;
    }

    public async Task<IActionResult> OnPostStartAsync(int? id)
    {
        if (id == null) return NotFound();
        var task = await _context.Tasks.Include(t => t.Sessions).ThenInclude(s => s.Pauses).FirstOrDefaultAsync(t => t.Id == id.Value);
        if (task == null) return NotFound();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!await IsUserClockedInForWorkAsync(_context, userId))
        {
            TempData["TasksClockInRequired"] = "You need to clock in before you can start this task. Clock in below, then press Start task again.";
            return RedirectToPage(new { id });
        }

        if (task.Sessions.Any(s => s.EndedAtUtc == null && s.UserId == userId))
        {
            TempData["TasksError"] = "You already have an active session for this task.";
            return RedirectToPage(new { id });
        }
        // User cannot have two tasks with a running (non-paused) session at once
        var otherRunningSession = await _context.TaskSessions
            .Include(s => s.Pauses)
            .FirstOrDefaultAsync(s => s.EndedAtUtc == null && s.UserId == userId && s.TaskId != id.Value && !s.Pauses.Any(p => p.EndUtc == null));
        if (otherRunningSession != null)
        {
            TempData["TasksError"] = "You have another task with time running. Pause or finish that task first.";
            return RedirectToPage(new { id });
        }

        task.Status = TaskItemStatus.InProgress;
        _context.TaskSessions.Add(new TaskSession
        {
            TaskId = task.Id,
            UserId = userId!,
            StartedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        TempData["TasksSuccess"] = "Task started.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostPauseAsync(int? id)
    {
        if (id == null) return NotFound();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var session = await _context.TaskSessions
            .Include(s => s.Task)
            .Include(s => s.Pauses)
            .FirstOrDefaultAsync(s => s.TaskId == id.Value && s.EndedAtUtc == null && s.UserId == userId);
        if (session == null)
        {
            TempData["TasksError"] = "No active session to pause.";
            return RedirectToPage(new { id });
        }
        if (session.Pauses.Any(p => p.EndUtc == null))
        {
            TempData["TasksError"] = "Session is already paused.";
            return RedirectToPage(new { id });
        }
        _context.TaskSessionPauses.Add(new TaskSessionPause
        {
            TaskSessionId = session.Id,
            StartUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        TempData["TasksSuccess"] = "Task paused.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostResumeAsync(int? id)
    {
        if (id == null) return NotFound();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!await IsUserClockedInForWorkAsync(_context, userId))
        {
            TempData["TasksClockInRequired"] = "You need to clock in before you can resume this task. Clock in below, then press Resume again.";
            return RedirectToPage(new { id });
        }

        var session = await _context.TaskSessions
            .Include(s => s.Task)
            .Include(s => s.Pauses)
            .FirstOrDefaultAsync(s => s.TaskId == id.Value && s.EndedAtUtc == null && s.UserId == userId);
        if (session == null)
        {
            TempData["TasksError"] = "No active session to resume.";
            return RedirectToPage(new { id });
        }
        var openPause = session.Pauses.FirstOrDefault(p => p.EndUtc == null);
        if (openPause == null)
        {
            TempData["TasksError"] = "Session is not paused.";
            return RedirectToPage(new { id });
        }
        openPause.EndUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        TempData["TasksSuccess"] = "Task resumed.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostFinishAsync(int? id)
    {
        if (id == null) return NotFound();
        var task = await _context.Tasks.Include(t => t.Sessions).FirstOrDefaultAsync(t => t.Id == id.Value);
        if (task == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var now = DateTime.UtcNow;

        // CTO issue #43: finishing is scoped to the current user's session;
        // the task is finished only when everyone has finished.
        var currentOpenSession = await _context.TaskSessions
            .Include(s => s.Pauses)
            .FirstOrDefaultAsync(s => s.TaskId == id.Value && s.EndedAtUtc == null && s.UserId == userId);

        if (currentOpenSession == null)
        {
            TempData["TasksError"] = "You don't have an active session for this task to finish.";
            return RedirectToPage(new { id });
        }

        currentOpenSession.EndedAtUtc = now;
        foreach (var p in currentOpenSession.Pauses.Where(p => p.EndUtc == null))
        {
            p.EndUtc = now;
        }

        await _context.SaveChangesAsync();

        var anyOtherOpenSessions = await _context.TaskSessions.AnyAsync(s => s.TaskId == id.Value && s.EndedAtUtc == null);
        task.Status = anyOtherOpenSessions ? TaskItemStatus.InProgress : TaskItemStatus.Finished;
        await _context.SaveChangesAsync();
        TempData["TasksSuccess"] = "Task finished.";
        return RedirectToPage(new { id });
    }
}
