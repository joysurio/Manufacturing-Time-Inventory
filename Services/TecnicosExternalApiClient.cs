using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ManufacturingTimeTracking.Services;

public class TecnicosExternalApiClient : ITecnicosExternalApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ExternalApiOptions _options;
    private readonly ILogger<TecnicosExternalApiClient> _logger;

    public TecnicosExternalApiClient(
        HttpClient httpClient,
        IOptions<ExternalApiOptions> options,
        ILogger<TecnicosExternalApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool?> TryGetIsActiveAsync(string user, CancellationToken cancellationToken = default)
    {
        var baseUrl = _options.BaseUrl?.TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
            return null;

        var requestUri = $"api/TecnicosApi/IsActive?user={Uri.EscapeDataString(user)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        if (!string.IsNullOrEmpty(_options.ApiKey))
            request.Headers.TryAddWithoutValidation("X-Api-Key", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errBody = string.Empty;
                try { errBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false); }
                catch { /* ignore */ }

                _logger.LogWarning(
                    "External API returned {Status} when checking IsActive for user {User}: {Body}",
                    response.StatusCode, user, errBody);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseIsActiveBody(body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External API request failed when checking IsActive for user {User}", user);
            return null;
        }
    }

    /// <summary>Parses API body; returns false when inactive or ambiguous (clock-out allowed).</summary>
    internal static bool ParseIsActiveBody(string body)
    {
        bool isActive = false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.True)
                return true;
            if (root.ValueKind == JsonValueKind.False)
                return false;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("isActive", out var ia) && ia.ValueKind == JsonValueKind.True)
                    return true;
                if (root.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.True)
                    return true;
                if (root.TryGetProperty("openTasksCount", out var cnt) && cnt.ValueKind == JsonValueKind.Number && cnt.GetInt32() > 0)
                    return true;
            }
            else if (root.ValueKind == JsonValueKind.Number)
            {
                return root.GetInt32() > 0;
            }
        }
        catch (JsonException)
        {
            if (bool.TryParse(body.Trim(), out var b))
                return b;
        }

        return isActive;
    }
}
