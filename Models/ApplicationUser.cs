using Microsoft.AspNetCore.Identity;

namespace ManufacturingTimeTracking.Models;

public class ApplicationUser : IdentityUser
{
    /// <summary>Employee first name. Shown in time tracking report and PDF export.</summary>
    public string? FirstName { get; set; }

    /// <summary>Employee last name. Shown in time tracking report and PDF export.</summary>
    public string? LastName { get; set; }

    /// <summary>National ID (e.g. DNI). Shown in time tracking report and PDF export.</summary>
    public string? Dni { get; set; }
}
