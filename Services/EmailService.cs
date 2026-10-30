using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ManufacturingTimeTracking.Services;

public interface IEmailService
{
    /// <summary>
    /// Sends an email asynchronously. The body is treated as HTML by default.
    /// </summary>
    Task SendEmailAsync(string to, string subject, string htmlBody, string? fromAddress = null, string? fromName = null);
}

/// <summary>
/// Simple SMTP email service using System.Net.Mail.SmtpClient.
/// Configuration via section "Smtp" in appsettings (Host, Port, EnableSsl, Username, Password, FromAddress, FromName).
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody, string? fromAddress = null, string? fromName = null)
    {
        var host = _config["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("SMTP host is not configured (Smtp:Host).");

        var port = 25;
        if (!string.IsNullOrWhiteSpace(_config["Smtp:Port"]) && int.TryParse(_config["Smtp:Port"], out var p))
            port = p;

        var enableSsl = _config.GetValue<bool?>("Smtp:EnableSsl") ?? true;

        var username = _config["Smtp:Username"];
        var password = _config["Smtp:Password"];

        var defaultFrom = _config["Smtp:FromAddress"] ?? "noreply@localhost";
        var defaultFromName = _config["Smtp:FromName"] ?? "ManufacturingTimeTracking";

        var mailFrom = fromAddress ?? defaultFrom;
        var mailFromName = fromName ?? defaultFromName;

        using var message = new MailMessage();
        message.From = new MailAddress(mailFrom, mailFromName);
        message.To.Add(new MailAddress(to));
        message.Subject = subject ?? string.Empty;
        message.Body = htmlBody ?? string.Empty;
        message.IsBodyHtml = true;

        using var client = new SmtpClient(host, port);
        try
        {
            client.EnableSsl = enableSsl;
            if (!string.IsNullOrWhiteSpace(username))
            {
                client.Credentials = new NetworkCredential(username, password ?? string.Empty);
            }

            // Optional: allow sending timeout configuration
            client.Timeout = (_config.GetValue<int?>("Smtp:TimeoutMs") ?? 100000);

            _logger.LogInformation("Sending email to {To} via SMTP {Host}:{Port} (SSL={Ssl})", to, host, port, enableSsl);
            await client.SendMailAsync(message);
            _logger.LogInformation("Email sent to {To}", to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", to);
            throw;
        }
    }
}
