using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ManufacturingTimeTracking.Services;
using Microsoft.AspNetCore.Authorization;

namespace ManufacturingTimeTracking.Pages.Admin;

[Authorize(Roles = "Admin")]
public class EmailSettingsModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly IEmailService _emailService;
    private readonly ILogger<EmailSettingsModel> _logger;

    public EmailSettingsModel(IConfiguration config, IWebHostEnvironment env, IEmailService emailService, ILogger<EmailSettingsModel> logger)
    {
        _config = config;
        _env = env;
        _emailService = emailService;
        _logger = logger;
    }

    [BindProperty]
    public string Host { get; set; } = "";
    [BindProperty]
    public int Port { get; set; } = 587;
    [BindProperty]
    public bool EnableSsl { get; set; } = true;
    [BindProperty]
    public string Username { get; set; } = "";
    [BindProperty]
    public string Password { get; set; } = "";
    [BindProperty]
    public string FromAddress { get; set; } = "";
    [BindProperty]
    public string FromName { get; set; } = "";
    [BindProperty]
    public int TimeoutMs { get; set; } = 100000;

    // Test email fields
    [BindProperty]
    public string TestRecipient { get; set; } = "";
    [BindProperty]
    public string TestSubject { get; set; } = "Test email";
    [BindProperty]
    public string TestBody { get; set; } = "<p>Test email from ManufacturingTimeTracking</p>";

    public string Message { get; set; } = "";

    public void OnGet()
    {
        // Load from IConfiguration
        Host = _config["Smtp:Host"] ?? string.Empty;
        if (int.TryParse(_config["Smtp:Port"], out var p)) Port = p;
        EnableSsl = _config.GetValue<bool?>("Smtp:EnableSsl") ?? true;
        Username = _config["Smtp:Username"] ?? string.Empty;
        // Do not prefill password for safety
        Password = string.Empty;
        FromAddress = _config["Smtp:FromAddress"] ?? string.Empty;
        FromName = _config["Smtp:FromName"] ?? string.Empty;
        TimeoutMs = _config.GetValue<int?>("Smtp:TimeoutMs") ?? 100000;
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        // Persist to appsettings.json (note: this modifies the file on disk).
        try
        {
            var basePath = _env.ContentRootPath;
            var path = Path.Combine(basePath, "appsettings.json");
            string json = System.IO.File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.Clone();

            var options = new JsonWriterOptions { Indented = true };
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, options))
            {
                writer.WriteStartObject();
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.NameEquals("Smtp")) continue; // skip existing Smtp
                    prop.WriteTo(writer);
                }
                // write Smtp
                writer.WritePropertyName("Smtp");
                writer.WriteStartObject();
                writer.WriteString("Host", Host ?? string.Empty);
                writer.WriteNumber("Port", Port);
                writer.WriteBoolean("EnableSsl", EnableSsl);
                writer.WriteString("Username", Username ?? string.Empty);
                writer.WriteString("Password", Password ?? string.Empty);
                writer.WriteString("FromAddress", FromAddress ?? string.Empty);
                writer.WriteString("FromName", FromName ?? string.Empty);
                writer.WriteNumber("TimeoutMs", TimeoutMs);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            var outJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            System.IO.File.WriteAllText(path, outJson);
            TempData["ToastrSuccess"] = "SMTP settings saved to appsettings.json. Restart may be required for changes to take effect.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save SMTP settings");
            TempData["ToastrError"] = "Failed to save SMTP settings: " + ex.Message;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostSendTestAsync()
    {
        if (string.IsNullOrWhiteSpace(TestRecipient))
        {
            TempData["ToastrError"] = "Please provide a test recipient email.";
            return RedirectToPage();
        }

        try
        {
            // Use the current IEmailService (reads configuration)
            await _emailService.SendEmailAsync(TestRecipient, TestSubject ?? "Test email", TestBody ?? "<p>Test</p>");
            TempData["ToastrSuccess"] = "Test email sent to " + TestRecipient;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test email failed");
            TempData["ToastrError"] = "Test email failed: " + ex.Message;
        }
        return RedirectToPage();
    }
}
