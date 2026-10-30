using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Models;

namespace ManufacturingTimeTracking.Pages.Admin.Users;

[Authorize(Policy = "CanManageUsers")]
public class EditModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public EditModel(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> RoleList { get; set; } = new();

    public class InputModel
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

        [Required(ErrorMessage = "Each user must have a role.")]
        [Display(Name = "Role")]
        public string Role { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
        [Display(Name = "New password (leave blank to keep current)")]
        public string? NewPassword { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm new password")]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
        public string? ConfirmPassword { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return NotFound();

        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return NotFound();

        UserId = user.Id;
        Email = user.Email ?? user.UserName ?? "";

        var roles = await _userManager.GetRolesAsync(user);
        var currentRole = roles.FirstOrDefault() ?? "Normal";

        RoleList = (await _roleManager.Roles.OrderBy(r => r.Name).ToListAsync())
            .Select(r => new SelectListItem(r.Name, r.Name!, r.Name == currentRole))
            .ToList();

        Input.Role = currentRole;
        Input.FirstName = user.FirstName;
        Input.LastName = user.LastName;
        Input.Dni = user.Dni;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return NotFound();

        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return NotFound();

        UserId = user.Id;
        Email = user.Email ?? user.UserName ?? "";

        RoleList = (await _roleManager.Roles.OrderBy(r => r.Name).ToListAsync())
            .Select(r => new SelectListItem(r.Name, r.Name!, r.Name == Input.Role))
            .ToList();

        if (string.IsNullOrWhiteSpace(Input.Role))
        {
            ModelState.AddModelError(nameof(Input.Role), "Each user must have a role.");
            return Page();
        }

        user.FirstName = Input.FirstName?.Trim();
        user.LastName = Input.LastName?.Trim();
        user.Dni = Input.Dni?.Trim();
        await _userManager.UpdateAsync(user);

        // Update role: remove all current roles, add the selected one
        var currentRoles = await _userManager.GetRolesAsync(user);
        if (currentRoles.Count > 0)
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
        var roleResult = await _userManager.AddToRoleAsync(user, Input.Role);
        if (!roleResult.Succeeded)
        {
            foreach (var err in roleResult.Errors)
                ModelState.AddModelError(nameof(Input.Role), err.Description);
            return Page();
        }

        // Optional password change (admin setting password directly)
        if (!string.IsNullOrWhiteSpace(Input.NewPassword))
        {
            // Remove existing password if any
            if (await _userManager.HasPasswordAsync(user))
            {
                var removeResult = await _userManager.RemovePasswordAsync(user);
                if (!removeResult.Succeeded)
                {
                    foreach (var err in removeResult.Errors)
                        ModelState.AddModelError(nameof(Input.NewPassword), err.Description);
                    return Page();
                }
            }
            
            // Add the new password
            var addPasswordResult = await _userManager.AddPasswordAsync(user, Input.NewPassword);
            if (!addPasswordResult.Succeeded)
            {
                foreach (var err in addPasswordResult.Errors)
                    ModelState.AddModelError(nameof(Input.NewPassword), err.Description);
                return Page();
            }
            
            // Update security stamp to invalidate existing sessions/tokens
            await _userManager.UpdateSecurityStampAsync(user);
        }

        TempData["Success"] = "User role and password updated.";
        return RedirectToPage("./Index");
    }
}
