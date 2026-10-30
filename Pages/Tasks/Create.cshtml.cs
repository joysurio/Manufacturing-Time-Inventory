using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;
using ManufacturingTimeTracking.Models.Tasks;

namespace ManufacturingTimeTracking.Pages.Tasks;

[Authorize]
public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public SelectList? UserSelectList { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "Title is required.")]
        [StringLength(256)]
        [Display(Name = "Task title")]
        public string Title { get; set; } = "";

        [Display(Name = "Description (optional)")]
        [StringLength(2000)]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Assign the task to a user.")]
        [Display(Name = "Assigned to")]
        public string AssignedUserId { get; set; } = "";
    }

    public async Task OnGetAsync()
    {
        await LoadUsersAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadUsersAsync();
        if (!ModelState.IsValid)
            return Page();

        var user = await _userManager.FindByIdAsync(Input.AssignedUserId);
        if (user == null)
        {
            ModelState.AddModelError(nameof(Input.AssignedUserId), "Selected user not found.");
            return Page();
        }

        var task = new TaskItem
        {
            Title = Input.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim(),
            AssignedUserId = Input.AssignedUserId,
            Status = TaskItemStatus.Open,
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.Tasks.Add(task);
        await _context.SaveChangesAsync();

        TempData["TasksSuccess"] = "Task created. You can open it to start tracking time.";
        return RedirectToPage("Detail", new { id = task.Id });
    }

    private async Task LoadUsersAsync()
    {
        var users = await _userManager.Users.OrderBy(u => u.Email).ToListAsync();
        var items = users.Select(u =>
        {
            var name = !string.IsNullOrWhiteSpace(u.FirstName) || !string.IsNullOrWhiteSpace(u.LastName)
                ? $"{u.FirstName} {u.LastName}".Trim()
                : u.UserName ?? u.Email ?? u.Id;
            return new SelectListItem($"{name} ({u.Email})", u.Id);
        }).ToList();
        UserSelectList = new SelectList(items, "Value", "Text", Input.AssignedUserId);
    }
}
