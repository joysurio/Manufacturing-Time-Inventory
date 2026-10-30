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
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public List<TaskRow> Tasks { get; set; } = new();

    /// <summary>All | Open | InProgress | Finished</summary>
    [BindProperty(SupportsGet = true)]
    public string Filter { get; set; } = "All";

    public class TaskRow
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public string AssignedUserId { get; set; } = "";
        public string AssignedUserName { get; set; } = "";
        public TaskItemStatus Status { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public TimeSpan TotalTimeSpent { get; set; }
    }

    public async Task OnGetAsync()
    {
        var query = _context.Tasks
            .AsNoTracking()
            .AsQueryable();

        if (Filter == "Open")
            query = query.Where(t => t.Status == TaskItemStatus.Open);
        else if (Filter == "InProgress")
            query = query.Where(t => t.Status == TaskItemStatus.InProgress);
        else if (Filter == "Finished")
            query = query.Where(t => t.Status == TaskItemStatus.Finished);

        var list = await query.OrderByDescending(t => t.CreatedAtUtc).ToListAsync();
        var userIds = list.Select(t => t.AssignedUserId).Distinct().ToList();
        var userNames = new Dictionary<string, string>();
        foreach (var uid in userIds)
        {
            var u = await _userManager.FindByIdAsync(uid);
            userNames[uid] = u != null
                ? (!string.IsNullOrWhiteSpace(u.FirstName) || !string.IsNullOrWhiteSpace(u.LastName) ? $"{u.FirstName} {u.LastName}".Trim() : u.UserName ?? u.Email ?? uid)
                : uid;
        }

        var taskIds = list.Select(t => t.Id).ToList();
        var allSessions = await _context.TaskSessions
            .AsNoTracking()
            .Where(s => taskIds.Contains(s.TaskId))
            .Include(s => s.Pauses)
            .ToListAsync();
        var sessionsByTask = allSessions.GroupBy(s => s.TaskId).ToDictionary(g => g.Key, g => g.ToList());
        var now = DateTime.UtcNow;
        Tasks = new List<TaskRow>();
        foreach (var t in list)
        {
            var sessions = sessionsByTask.GetValueOrDefault(t.Id) ?? new List<TaskSession>();
            var total = TimeSpan.Zero;
            foreach (var s in sessions)
            {
                var openPause = s.Pauses.FirstOrDefault(p => p.EndUtc == null);
                var effectiveEnd = s.EndedAtUtc ?? (openPause != null ? openPause.StartUtc : now);
                var sessionSpan = effectiveEnd - s.StartedAtUtc;
                var pauseTotal = TimeSpan.Zero;
                foreach (var p in s.Pauses)
                {
                    if (p.EndUtc.HasValue && p.EndUtc.Value <= effectiveEnd)
                        pauseTotal += p.EndUtc.Value - p.StartUtc;
                }
                total += sessionSpan - pauseTotal;
            }

            Tasks.Add(new TaskRow
            {
                Id = t.Id,
                Title = t.Title,
                Description = t.Description,
                AssignedUserId = t.AssignedUserId,
                AssignedUserName = userNames.GetValueOrDefault(t.AssignedUserId, t.AssignedUserId),
                Status = t.Status,
                CreatedAtUtc = t.CreatedAtUtc,
                TotalTimeSpent = total
            });
        }
    }

    public async Task<IActionResult> OnPostReopenAsync(int id)
    {
        var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task == null)
            return RedirectToPage();

        if (task.Status == TaskItemStatus.Finished)
        {
            task.Status = TaskItemStatus.Open;
            await _context.SaveChangesAsync();
        }

        return RedirectToPage(new { Filter });
    }
}
