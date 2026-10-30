using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ManufacturingTimeTracking.Services;

namespace ManufacturingTimeTracking.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "CanUseInventory")]
public class OcrController : ControllerBase
{
    private readonly OpenAIVisionService _visionService;
    private readonly ILogger<OcrController> _logger;

    public OcrController(OpenAIVisionService visionService, ILogger<OcrController> logger)
    {
        _visionService = visionService;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Post(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No image file provided." });

        var contentType = file.ContentType ?? "";
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "File must be an image." });

        if (!_visionService.IsConfigured)
        {
            return StatusCode(503, new
            {
                error = "GPT OCR not configured.",
                hint = "Add OpenAI:ApiKey and OpenAI:Model (e.g. gpt-4o) in appsettings.json."
            });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _visionService.GetOcrAndSupposedItemFromImageAsync(stream, contentType, cancellationToken);

            if (result == null)
                return StatusCode(502, new { error = "GPT Vision failed. Check OpenAI key and model." });

            return Ok(new { text = result.OcrText, supposedItem = result.SupposedItem });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR by GPT failed");
            return StatusCode(500, new { error = "Error processing image." });
        }
    }
}
