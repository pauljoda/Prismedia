using SkiaSharp;

namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// Composes two to four already-resolved child thumbnails into one deterministic JPEG.
/// Sources are center-cropped into a two-up, feature-plus-two, or four-up layout so the
/// result fills the owning Entity kind's canonical thumbnail aspect ratio.
/// </summary>
public sealed class SkiaThumbnailCollageComposer {
    /// <summary>Writes a collage and returns whether a decodable output was produced.</summary>
    public bool Compose(
        IReadOnlyList<string> sourcePaths,
        string outputPath,
        int width,
        int height,
        int quality) {
        if (sourcePaths.Count is < 2 or > 4 || width <= 0 || height <= 0) {
            return false;
        }

        var bitmaps = sourcePaths
            .Select(path => File.Exists(path) ? SKBitmap.Decode(path) : null)
            .ToArray();
        if (bitmaps.Any(bitmap => bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)) {
            foreach (var bitmap in bitmaps) bitmap?.Dispose();
            return false;
        }

        try {
            using var canvasBitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(canvasBitmap);
            canvas.Clear(new SKColor(12, 12, 14));
            using var paint = new SKPaint {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.High
            };

            var destinations = Layout(sourcePaths.Count, width, height);
            for (var index = 0; index < bitmaps.Length; index++) {
                DrawCover(canvas, bitmaps[index]!, destinations[index], paint);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var image = SKImage.FromBitmap(canvasBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 1, 100));
            if (data is null) return false;
            using var output = File.Create(outputPath);
            data.SaveTo(output);
            return true;
        } finally {
            foreach (var bitmap in bitmaps) bitmap?.Dispose();
        }
    }

    private static SKRect[] Layout(int count, int width, int height) {
        var halfWidth = width / 2f;
        var halfHeight = height / 2f;
        return count switch {
            2 => [
                new SKRect(0, 0, halfWidth, height),
                new SKRect(halfWidth, 0, width, height)
            ],
            3 => [
                new SKRect(0, 0, halfWidth, height),
                new SKRect(halfWidth, 0, width, halfHeight),
                new SKRect(halfWidth, halfHeight, width, height)
            ],
            _ => [
                new SKRect(0, 0, halfWidth, halfHeight),
                new SKRect(halfWidth, 0, width, halfHeight),
                new SKRect(0, halfHeight, halfWidth, height),
                new SKRect(halfWidth, halfHeight, width, height)
            ]
        };
    }

    private static void DrawCover(SKCanvas canvas, SKBitmap bitmap, SKRect destination, SKPaint paint) {
        var sourceAspect = bitmap.Width / (float)bitmap.Height;
        var destinationAspect = destination.Width / destination.Height;
        SKRect source;
        if (sourceAspect > destinationAspect) {
            var sourceWidth = bitmap.Height * destinationAspect;
            var left = (bitmap.Width - sourceWidth) / 2f;
            source = new SKRect(left, 0, left + sourceWidth, bitmap.Height);
        } else {
            var sourceHeight = bitmap.Width / destinationAspect;
            var top = (bitmap.Height - sourceHeight) / 2f;
            source = new SKRect(0, top, bitmap.Width, top + sourceHeight);
        }

        canvas.DrawBitmap(bitmap, source, destination, paint);
    }
}
