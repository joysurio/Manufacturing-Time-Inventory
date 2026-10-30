using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ManufacturingTimeTracking.Services;

/// <summary>
/// Sends a text prompt to the OpenAI Chat Completions API and returns the generated text.
/// Uses the same OpenAI:ApiKey already used by OpenAIVisionService.
/// </summary>
public class GptReportService
{
    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GptReportService> _logger;

    public GptReportService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<GptReportService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["OpenAI:ApiKey"]);

    /// <summary>
    /// Sends <paramref name="systemPrompt"/> (optional) + <paramref name="userMessage"/> to GPT and returns the reply text.
    /// </summary>
    public async Task<(string? Result, string? Error)> GenerateAsync(
        string userMessage,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["OpenAI:ApiKey"]?.Trim();
        if (string.IsNullOrEmpty(apiKey))
            return (null, "OpenAI API key is not configured. Add OpenAI:ApiKey to appsettings.");

        var model = _configuration["OpenAI:ReportModel"] ?? _configuration["OpenAI:Model"] ?? "gpt-4o";
        var maxTokens = _configuration.GetValue<int>("OpenAI:ReportMaxTokens", 3000);

        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new { role = "system", content = systemPrompt });
        messages.Add(new { role = "user", content = userMessage });

        var requestBody = new
        {
            model,
            max_tokens = maxTokens,
            messages
        };

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        client.Timeout = TimeSpan.FromSeconds(120);

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync(ApiUrl, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GPT API error {StatusCode}: {Body}", response.StatusCode, body);
                var errMsg = TryExtractOpenAiError(body);
                return (null, $"GPT API error ({response.StatusCode}): {errMsg}");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return (null, "GPT returned no choices.");

            var text = choices[0]
                .GetProperty("message")
                .TryGetProperty("content", out var c) ? c.GetString() : null;

            return (text, null);
        }
        catch (TaskCanceledException)
        {
            return (null, "The request to GPT timed out. Try a smaller date range or fewer records.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GPT report request failed");
            return (null, $"Unexpected error: {ex.Message}");
        }
    }

    private static string TryExtractOpenAiError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("message", out var msg))
                    return msg.GetString() ?? body;
            }
        }
        catch { }
        return body.Length > 300 ? body[..300] + "…" : body;
    }
}
