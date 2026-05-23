using TOfont.Core.Models;

namespace TOfont.Core.Parsing;

public class FontImporter
{
    public static List<GlyphInfo> Import(string filePath, int width, int height, int offset = 0)
    {
        var bytes = File.ReadAllBytes(filePath);
        var bytesPerChar = (width * height + 7) / 8;

        var result = new List<GlyphInfo>();
        var idx = 0;

        for (var pos = offset; pos + bytesPerChar <= bytes.Length; pos += bytesPerChar)
        {
            var charData = new byte[bytesPerChar];
            Array.Copy(bytes, pos, charData, 0, bytesPerChar);

            result.Add(new GlyphInfo
            {
                Character = (char)(idx < 128 ? idx + 32 : idx),
                Width = width,
                Height = height,
                DotData = charData
            });
            idx++;
        }

        return result;
    }
}
