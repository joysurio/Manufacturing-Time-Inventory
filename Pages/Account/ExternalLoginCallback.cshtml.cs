using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ManufacturingTimeTracking.Data;
using ManufacturingTimeTracking.Models;

namespace ManufacturingTimeTracking.Pages.Account;

public class ExternalLoginCallbackModel : PageModel
{
    /// <summary>Corporate workspace: only these Google accounts may use external login when config does not override.</summary>
    private const string DefaultAllowedGoogleEmailDomain = "lafabricadesoftware.com";

    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ExternalLoginCallbackModel> _logger;

    public ExternalLoginCallbackModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ApplicationDbContext context,
        IConfiguration configuration,
        ILogger<ExternalLoginCallbackModel> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _roleManager = roleManager;
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Effective allowed email domain for Google sign-in (without leading @).
    /// <see cref="DefaultAllowedGoogleEmailDomain"/> is used when <c>Google:AllowedDomain</c> is not set,
    /// so restriction is always enforced in production even if appsettings omits the key.
    /// </summary>
    private static string GetEffectiveGoogleAllowedEmailDomain(IConfiguration configuration)
    {
        var raw = configuration["Google:AllowedDomain"]?.Trim();
        if (string.IsNullOrEmpty(raw))
            return DefaultAllowedGoogleEmailDomain;
        return raw.StartsWith("@", StringComparison.Ordinal) ? raw[1..] : raw;
    }

    private static bool IsEmailAllowedForGoogleSignIn(string email, string domainWithoutAt)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(domainWithoutAt))
            return false;
        return email.EndsWith("@" + domainWithoutAt, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl ??= Url.Content("~/");

        if (!string.IsNullOrEmpty(remoteError))
        {
            TempData["ToastrError"] = $"Error from external provider: {remoteError}";
            return RedirectToPage("./Login", new { returnUrl });
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            _logger.LogWarning("GetExternalLoginInfoAsync returned null.");
            TempData["ToastrError"] = "Error loading external login information. Please try again.";
            return RedirectToPage("./Login", new { returnUrl });
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(email))
        {
            TempData["ToastrError"] = "Could not read email from Google account.";
            return RedirectToPage("./Login", new { returnUrl });
        }

        // Only @lafabricadesoftware.com (or Google:AllowedDomain override) may sign in with Google
        var allowedDomain = GetEffectiveGoogleAllowedEmailDomain(_configuration);
        if (!IsEmailAllowedForGoogleSignIn(email, allowedDomain))
        {
            await _signInManager.SignOutAsync();
            _logger.LogWarning("Google sign-in rejected for email outside allowed domain {Domain}: {Email}", allowedDomain, email);
            TempData["ToastrError"] = $"Access denied. Sign in with a Google account that ends with @{allowedDomain}.";
            return RedirectToPage("./Login", new { returnUrl });
        }

        // Returning user: try external login sign-in first (already linked)
        var result = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: true, bypassTwoFactor: true);

        if (result.Succeeded)
        {
            _logger.LogInformation("{Email} signed in with {Provider}.", email, info.LoginProvider);
            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                var activeRun = await _context.StepRuns
                    .Include(r => r.StepExec)
                    .Where(r => r.UserId == existingUser.Id && r.FinishedAt == null)
                    .FirstOrDefaultAsync();
                if (activeRun != null)
                {
                    var stepTitle = activeRun.StepExec?.Title ?? "a step";
                    TempData["ToastrInfo"] = $"You have \"{stepTitle}\" in progress. Redirecting...";
                    return RedirectToPage("/Orders/StepView", new { stepExecId = activeRun.StepExecId });
                }
            }
            TempData["ToastrSuccess"] = $"Signed in as {email}.";
            return LocalRedirect(returnUrl);
        }

        // New user: auto-provision account
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };
            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
                _logger.LogWarning("Create user failed for {Email}: {Errors}", email, errors);
                TempData["ToastrError"] = "Could not create your account. Please contact support.";
                return RedirectToPage("./Login", new { returnUrl });
            }

            // Google sign-in is restricted to the corporate domain above; new users get Operator by default
            const string defaultRole = "Operator";
            if (!await _roleManager.RoleExistsAsync(defaultRole))
                await _roleManager.CreateAsync(new IdentityRole(defaultRole));
            await _userManager.AddToRoleAsync(user, defaultRole);
            _logger.LogInformation("User auto-provisioned from Google: {Email}, role {Role}.", email, defaultRole);
        }

        // Link Google account to user
        var addLoginResult = await _userManager.AddLoginAsync(user, info);
        if (!addLoginResult.Succeeded)
        {
            _logger.LogWarning("Could not link Google login for {Email}: {Errors}", email,
                string.Join("; ", addLoginResult.Errors.Select(e => e.Description)));
        }

        await _signInManager.SignInAsync(user, isPersistent: true, info.LoginProvider);

        var newUserActiveRun = await _context.StepRuns
            .Include(r => r.StepExec)
            .Where(r => r.UserId == user.Id && r.FinishedAt == null)
            .FirstOrDefaultAsync();
        if (newUserActiveRun != null)
        {
            var stepTitle = newUserActiveRun.StepExec?.Title ?? "a step";
            TempData["ToastrInfo"] = $"You have \"{stepTitle}\" in progress. Redirecting...";
            return RedirectToPage("/Orders/StepView", new { stepExecId = newUserActiveRun.StepExecId });
        }

        TempData["ToastrSuccess"] = $"Account created and signed in as {email}. Welcome!";
        return LocalRedirect(returnUrl);
    }
}
