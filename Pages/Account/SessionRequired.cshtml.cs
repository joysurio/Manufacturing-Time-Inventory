using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ManufacturingTimeTracking.Pages.Account;

public class SessionRequiredModel : PageModel
{
    public string ReturnUrl { get; private set; } = "/";

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? Url.Content("~/")! : returnUrl;
    }
}
