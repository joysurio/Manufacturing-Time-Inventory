using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using ManufacturingTimeTracking.Services;

namespace ManufacturingTimeTracking.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ApprovalsController : ControllerBase
{
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<ApprovalsController> _logger;

    public ApprovalsController(IEmailService emailService, IConfiguration config, ILogger<ApprovalsController> logger)
    {
        _emailService = emailService;
        _config = config;
        _logger = logger;
    }

    public class ApproveRequest
    {
        public string? Action { get; set; }
        public string? Target { get; set; }
        public string? TemplateId { get; set; }
        public string? ModalId { get; set; }
        public string? Password { get; set; }
    }

    [HttpPost("authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize([FromBody] ApproveRequest req)
    {
        try
        {
                // Authorize called
            // Check role OR password
            var user = HttpContext.User;
            var userName = user?.Identity?.Name ?? user?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "(anonymous)";
            var isSupervisor = user?.IsInRole("Supervisor") == true || user?.IsInRole("Admin") == true;
            var secret = _config["ResponsibleApprovalSecret"] ?? "paso autorizado por jairo";
            var provided = req?.Password;
            var providedTrimmed = provided?.Trim();
            var okByPassword = !string.IsNullOrEmpty(providedTrimmed) && providedTrimmed == secret;
            _logger.LogInformation("Approval attempt by {User}. isSupervisor={IsSupervisor}, passwordProvided={HasPwd}", userName, isSupervisor, !string.IsNullOrEmpty(provided));
            // Do not log sensitive debug info in production

            if (!isSupervisor)
            {
                if (string.IsNullOrEmpty(provided))
                {
                    _logger.LogWarning("Approval denied: no password provided and user not supervisor");
                    return StatusCode(403, new { error = "password_required", message = "Password is required when the user is not supervisor." });
                }
                if (!okByPassword)
                {
                    _logger.LogWarning("Approval denied: provided password did not match");
                    return StatusCode(403, new { error = "password_incorrect", message = "Provided password is incorrect." });
                }
            }

            // Compose email
            var recipients = _config.GetSection("IncidentRecipients").Get<string[]>() ?? Array.Empty<string>();
            var subject = $"Action authorized: {req?.Action ?? "unknown"}";
            // userName already computed above
            var when = DateTime.UtcNow.ToString("u");
            var body = $"<p>The following action was authorized by <strong>{userName}</strong> at {when} UTC.</p>" +
                       $"<p>Action: <strong>{System.Net.WebUtility.HtmlEncode(req?.Action)}</strong></p>" +
                       $"<p>Target: {System.Net.WebUtility.HtmlEncode(req?.Target ?? req?.TemplateId ?? req?.ModalId ?? string.Empty)}</p>" +
                       $"<p>If this was unexpected please investigate.</p>";

            foreach (var to in recipients)
            {
                try
                {
                    await _emailService.SendEmailAsync(to, subject, body);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send approval notification to {To}", to);
                }
            }

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Approval API error");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
