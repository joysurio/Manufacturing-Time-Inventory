using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ManufacturingTimeTracking.Controllers;

public class HomeController : Controller
{
    /// <summary>MVC default route target; redirect to main app (Razor Pages Index).</summary>
    [AllowAnonymous]
    public IActionResult Index()
    {
        return Redirect("~/");
    }
}
