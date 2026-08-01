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
                // ASCII 字库：索引 0..94 对应 ASCII 32..126（可见字符）
                // 非 ASCII 范围（如中文字库）字符码未知，用 '?' 占位，避免误导
                Character = idx < 95 ? (char)(idx + 32) : '?',
                Width = width,
                Height = height,
                DotData = charData
            });
            idx++;
        }

        return result;
    }
}
