using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using TOfont.Core.Models;

namespace TOfont.Core.Extraction;

[SupportedOSPlatform("windows")]
public class FontExtractor
{
    public int FontSize { get; set; } = 16;
    public bool Bold { get; set; }
    public bool Italic { get; set; }

    public GlyphInfo Extract(char character, string fontFamily)
    {
        var bmpW = FontSize * 3;
        var bmpH = FontSize * 3;
        using var bitmap = new Bitmap(bmpW, bmpH, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;

        using var font = new Font(fontFamily, FontSize,
            (Bold ? FontStyle.Bold : FontStyle.Regular) |
            (Italic ? FontStyle.Italic : FontStyle.Regular));

        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var sf = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near
        };

        g.DrawString(character.ToString(), font, Brushes.Black, 0, 0, sf);

        var rect = new Rectangle(0, 0, bmpW, bmpH);
        var bmpData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var rawLen = bmpData.Stride * bmpH;
        var raw = new byte[rawLen];
        Marshal.Copy(bmpData.Scan0, raw, 0, rawLen);
        bitmap.UnlockBits(bmpData);

        var stride = bmpData.Stride;

        int left = bmpW, top = bmpH, right = 0, bottom = 0;
        var found = false;

        for (var y = 0; y < bmpH; y++)
        {
            var rowOff = y * stride;
            for (var x = 0; x < bmpW; x++)
            {
                var off = rowOff + x * 4;
                var b = raw[off];
                var gr = raw[off + 1];
                var r = raw[off + 2];
                if ((r + gr + b) / 3f >= 200) continue;
                if (x < left) left = x;
                if (y < top) top = y;
                if (x > right) right = x;
                if (y > bottom) bottom = y;
                found = true;
            }
        }

        if (!found)
            return new GlyphInfo { Character = character, Width = 0, Height = 0, DotData = [] };

        var width = right - left + 1;
        var height = bottom - top + 1;
        var dotMatrix = new DotMatrix(width, height);

        for (var y = 0; y < height; y++)
        {
            var rowOff = (top + y) * stride;
            for (var x = 0; x < width; x++)
            {
                var off = rowOff + (left + x) * 4;
                var sum = raw[off] + raw[off + 1] + raw[off + 2];
                if (sum / 3f < 200)
                    dotMatrix.SetPixel(x, y, true);
            }
        }

        return new GlyphInfo
        {
            Character = character,
            Width = width,
            Height = height,
            DotData = dotMatrix.Data
        };
    }

    public List<GlyphInfo> ExtractRange(string characters, string fontFamily)
    {
        var result = new GlyphInfo[characters.Length];
        Parallel.For(0, characters.Length, i =>
        {
            result[i] = Extract(characters[i], fontFamily);
        });
        return new List<GlyphInfo>(result);
    }
}
