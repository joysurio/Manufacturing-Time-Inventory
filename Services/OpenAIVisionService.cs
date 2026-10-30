using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ManufacturingTimeTracking.Services;

public class OpenAIVisionService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAIVisionService> _logger;

    private const string VisionApiUrl = "https://api.openai.com/v1/chat/completions";

    public OpenAIVisionService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<OpenAIVisionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["OpenAI:ApiKey"]);

    public async Task<OcrAndItemResult?> GetOcrAndSupposedItemFromImageAsync(Stream imageStream, string contentType, CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["OpenAI:ApiKey"]?.Trim();
        if (string.IsNullOrEmpty(apiKey))
            return null;

        var model = _configuration["OpenAI:Model"] ?? "gpt-4o";
        var maxTokens = _configuration.GetValue<int>("OpenAI:MaxTokens", 1000);

        byte[] bytes;
        await using (var ms = new MemoryStream())
        {
            await imageStream.CopyToAsync(ms, cancellationToken);
            bytes = ms.ToArray();
        }

        var mediaType = contentType?.Split(';')[0]?.Trim() ?? "image/jpeg";
        try
        {
            await using var input = new MemoryStream(bytes);
            bytes = await OcrImagePreprocessor.PreprocessForOcrAsync(input, cancellationToken);
            mediaType = "image/png";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "OCR preprocessing failed, using original image");
        }

        var base64 = Convert.ToBase64String(bytes);
        var dataUrl = $"data:{mediaType};base64,{base64}";

        var prompt = @"Look at this image. Do two things:

1) OCR: Extract ALL visible text from the image in reading order. Put it in ""ocrText"". Preserve line breaks (use \n in JSON for each new line). Include brand, model number, ratings, serial number, certifications, URL—everything visible. If there is no text, use """".

2) Name/Type: Put in ""supposedItem"" the product type or name as shown on the label when clearly visible (e.g. ""STEPPER MOTOR"", ""NEMA 23 Stepper Motor""). If no product name is on the label, use one short phrase that describes the component (e.g. Motor, Box, Electric component, Cable, Sensor).

Reply with ONLY a valid JSON object, no other text or markdown. Example: {""ocrText"":""STEPPERONLINE\nSTEPPER MOTOR\n23HE30-2804S 2.8A 2Nm\nS/N: 2025060900175\nMADE IN CHINA"",""supposedItem"":""STEPPER MOTOR""}";

        var requestBody = new
        {
            model,
            max_tokens = maxTokens,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = prompt },
                        new { type = "image_url", image_url = new { url = dataUrl, detail = "high" } }
                    }
                }
            }
        };

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        client.Timeout = TimeSpan.FromSeconds(90);

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync(VisionApiUrl, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenAI Vision API error {StatusCode}: {Body}", response.StatusCode, responseBody);
                return null;
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return null;

            var message = choices[0].GetProperty("message");
            var text = message.TryGetProperty("content", out var contentNode) ? contentNode.GetString() : null;
            if (string.IsNullOrWhiteSpace(text))
                return null;

            text = text.Trim();
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*```[\w]*\s*\n?", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\n?\s*```\s*$", "");
            text = text.Trim();

            return ParseOcrAndItemResponse(text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI Vision request failed");
            return null;
        }
    }

    private static OcrAndItemResult ParseOcrAndItemResponse(string text)
    {
        try
        {
            using var parsed = JsonDocument.Parse(text);
            var root = parsed.RootElement;
            var ocrText = root.TryGetProperty("ocrText", out var ocrNode) ? ocrNode.GetString() ?? "" : "";
            var supposedItem = root.TryGetProperty("supposedItem", out var itemNode) ? itemNode.GetString()?.Trim() : null;
            if (string.IsNullOrWhiteSpace(supposedItem))
                supposedItem = null;
            return new OcrAndItemResult { OcrText = ocrText ?? "", SupposedItem = supposedItem };
        }
        catch
        {
            return new OcrAndItemResult { OcrText = text, SupposedItem = null };
        }
    }
}
