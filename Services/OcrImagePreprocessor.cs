using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ManufacturingTimeTracking.Services;

/// <summary>
/// Preprocesses images for better OCR: reduces texture noise and improves contrast.
/// </summary>
public static class OcrImagePreprocessor
{
    public static async Task<byte[]> PreprocessForOcrAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        await using var ms = new MemoryStream();
        await imageStream.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;

        using var image = await Image.LoadAsync<Rgba32>(ms, cancellationToken);

        image.Mutate(x =>
        {
            x.Grayscale();
            x.BinaryThreshold(0.5f);
            x.Invert();
        });

        var outStream = new MemoryStream();
        await image.SaveAsPngAsync(outStream, cancellationToken);
        return outStream.ToArray();
    }
}
