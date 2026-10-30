using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ManufacturingTimeTracking.Models;

namespace ManufacturingTimeTracking.Pages.Account;

public class ExternalLoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;

    public ExternalLoginModel(SignInManager<ApplicationUser> signInManager)
    {
        _signInManager = signInManager;
    }

    public IActionResult OnGet(string? provider, string? returnUrl = null)
    {
        if (string.IsNullOrEmpty(provider) || !string.Equals(provider, "Google", StringComparison.OrdinalIgnoreCase))
            return RedirectToPage("./Login");

        var redirectUrl = Url.Page("./ExternalLoginCallback", pageHandler: null, values: new { returnUrl });
        // ConfigureExternalAuthenticationProperties sets Items["LoginProvider"] which GetExternalLoginInfoAsync requires to return non-null.
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return new ChallengeResult(provider, properties);
    }
}
