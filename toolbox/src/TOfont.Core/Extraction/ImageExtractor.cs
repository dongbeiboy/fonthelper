using SkiaSharp;
using TOfont.Core.Models;

namespace TOfont.Core.Extraction;

/// <summary>
/// 图片取模器 — 将图片二值化并转换为点阵
/// </summary>
public class ImageExtractor
{
    public int Threshold { get; set; } = 128;
    public int? TargetWidth { get; set; }
    public int? TargetHeight { get; set; }
    public int CropX { get; set; }
    public int CropY { get; set; }
    public int? CropWidth { get; set; }
    public int? CropHeight { get; set; }

    public DotMatrix Extract(string imagePath)
    {
        using var input = File.OpenRead(imagePath);
        using var codec = SKCodec.Create(input);
        using var original = SKBitmap.Decode(codec);

        SKBitmap source;
        if (CropWidth.HasValue && CropHeight.HasValue)
        {
            // 裁剪区域限制在图像范围内，越界时返回空矩阵，避免负尺寸崩溃
            var maxW = Math.Max(original.Width - CropX, 0);
            var maxH = Math.Max(original.Height - CropY, 0);
            var cw = Math.Min(Math.Max(CropWidth.Value, 0), maxW);
            var ch = Math.Min(Math.Max(CropHeight.Value, 0), maxH);
            if (cw <= 0 || ch <= 0)
                return new DotMatrix(0, 0);

            source = new SKBitmap(cw, ch);
            using var canvas = new SKCanvas(source);
            canvas.DrawBitmap(original,
                new SKRect(CropX, CropY, CropX + cw, CropY + ch),
                new SKRect(0, 0, cw, ch));
        }
        else
        {
            source = original;
        }

        var width = TargetWidth ?? source.Width;
        var height = TargetHeight ?? source.Height;

        using var resized = source.Width != width || source.Height != height
            ? source.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            : source;

        var matrix = new DotMatrix(width, height);

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var pixel = resized!.GetPixel(x, y);
            var gray = pixel.Red * 0.299f + pixel.Green * 0.587f + pixel.Blue * 0.114f;
            matrix.SetPixel(x, y, gray < Threshold);
        }

        if (CropWidth.HasValue) source.Dispose();
        return matrix;
    }
}
