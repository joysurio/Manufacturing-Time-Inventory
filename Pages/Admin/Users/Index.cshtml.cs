using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Models;

namespace ManufacturingTimeTracking.Pages.Admin.Users;

[Authorize(Policy = "CanManageUsers")]
public class IndexModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public IndexModel(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public IList<UserRoleViewModel> Users { get; set; } = new List<UserRoleViewModel>();

    [BindProperty]
    public CreateUserInputModel CreateUserInput { get; set; } = new();
    public List<SelectListItem> RoleList { get; set; } = new();
    public bool ShowCreateUserModal { get; set; }

    public class UserRoleViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Dni { get; set; }
        public string Roles { get; set; } = string.Empty;
    }

    public class CreateUserInputModel
    {
        [Display(Name = "First name")]
        [StringLength(256)]
        public string? FirstName { get; set; }

        [Display(Name = "Last name")]
        [StringLength(256)]
        public string? LastName { get; set; }

        [Display(Name = "DNI")]
        [StringLength(64)]
        public string? Dni { get; set; }

        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "User name (optional)")]
        [StringLength(256)]
        public string? UserName { get; set; }

        [Required(ErrorMessage = "Password is required.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "A role is required.")]
        [Display(Name = "Role")]
        public string Role { get; set; } = string.Empty;
    }

    public async Task OnGetAsync()
    {
        await LoadUsersAndRolesAsync();
    }

    public async Task<IActionResult> OnPostCreateUserAsync()
    {
        await LoadRoleListAsync();

        if (!ModelState.IsValid)
        {
            await LoadUsersOnlyAsync();
            ShowCreateUserModal = true;
            return Page();
        }

        var userName = string.IsNullOrWhiteSpace(CreateUserInput.UserName) ? CreateUserInput.Email : CreateUserInput.UserName.Trim();
        if (await _userManager.FindByEmailAsync(CreateUserInput.Email) != null)
        {
            ModelState.AddModelError("CreateUserInput.Email", "A user with this email already exists.");
            await LoadUsersOnlyAsync();
            ShowCreateUserModal = true;
            return Page();
        }
        if (await _userManager.FindByNameAsync(userName) != null && !string.IsNullOrWhiteSpace(CreateUserInput.UserName))
        {
            ModelState.AddModelError("CreateUserInput.UserName", "A user with this user name already exists.");
            await LoadUsersOnlyAsync();
            ShowCreateUserModal = true;
            return Page();
        }

        var user = new ApplicationUser
        {
            UserName = userName,
            Email = CreateUserInput.Email,
            EmailConfirmed = false,
            FirstName = CreateUserInput.FirstName?.Trim(),
            LastName = CreateUserInput.LastName?.Trim(),
            Dni = CreateUserInput.Dni?.Trim()
        };

        var createResult = await _userManager.CreateAsync(user, CreateUserInput.Password);
        if (!createResult.Succeeded)
        {
            foreach (var err in createResult.Errors)
                ModelState.AddModelError(string.Empty, err.Description);
            await LoadUsersOnlyAsync();
            ShowCreateUserModal = true;
            return Page();
        }

        var roleResult = await _userManager.AddToRoleAsync(user, CreateUserInput.Role);
        if (!roleResult.Succeeded)
        {
            foreach (var err in roleResult.Errors)
                ModelState.AddModelError("CreateUserInput.Role", err.Description);
            await LoadUsersOnlyAsync();
            ShowCreateUserModal = true;
            return Page();
        }

        TempData["ToastrSuccess"] = "User created successfully. They can now sign in with their email and password.";
        return RedirectToPage("./Index");
    }

    private async Task LoadUsersAndRolesAsync()
    {
        await LoadUsersOnlyAsync();
        await LoadRoleListAsync();
    }

    private async Task LoadUsersOnlyAsync()
    {
        var allUsers = await _userManager.Users.OrderBy(u => u.Email).ToListAsync();
        var allViewModels = new List<UserRoleViewModel>();
        foreach (var user in allUsers)
        {
            var roles = await _userManager.GetRolesAsync(user);
            allViewModels.Add(new UserRoleViewModel
            {
                Id = user.Id,
                Email = user.Email ?? "",
                UserName = user.UserName ?? "",
                FirstName = user.FirstName,
                LastName = user.LastName,
                Dni = user.Dni,
                Roles = roles.Any() ? string.Join(", ", roles) : "—"
            });
        }

        Users = allViewModels;
    }

    private async Task LoadRoleListAsync()
    {
        RoleList = (await _roleManager.Roles.OrderBy(r => r.Name).ToListAsync())
            .Select(r => new SelectListItem(r.Name, r.Name!))
            .ToList();
    }
}
