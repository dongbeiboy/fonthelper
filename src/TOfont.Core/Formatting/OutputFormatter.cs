using System.Text;
using TOfont.Core.Models;

namespace TOfont.Core.Formatting;

/// <summary>
/// 输出格式化器 — 将点阵数据格式化为 C 代码等输出
/// </summary>
public class OutputFormatter
{
    private readonly OutputFormat _format;

    public OutputFormatter(OutputFormat format)
    {
        _format = format;
    }

    /// <summary>
    /// 格式化单个字形为 C 字节数组
    /// </summary>
    public string FormatGlyph(GlyphInfo glyph, string? name = null)
    {
        var sb = new StringBuilder();
        var dataStr = FormatBytes(glyph.DotData);

        var comment = string.IsNullOrEmpty(_format.Comment)
            ? ""
            : $" {_format.Comment} '{glyph.Character}' (U+{(int)glyph.Character:X4}), {glyph.Width}×{glyph.Height}";

        var line = _format.LineTemplate
            .Replace("{prefix}", _format.Prefix)
            .Replace("{name}", name ?? $"char_{(int)glyph.Character:X4}")
            .Replace("{data}", dataStr)
            .Replace("{suffix}", _format.Suffix)
            .Replace("{comment}", comment);

        sb.AppendLine(line);

        if (!string.IsNullOrEmpty(_format.Comment))
        {
            sb.AppendLine($"{_format.Comment} Width: {glyph.Width}, Height: {glyph.Height}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 格式化多个字形
    /// </summary>
    public string FormatGlyphs(IEnumerable<GlyphInfo> glyphs)
    {
        var sb = new StringBuilder();
        foreach (var glyph in glyphs)
        {
            sb.Append(FormatGlyph(glyph));
        }
        return sb.ToString();
    }

    private string FormatBytes(byte[] data)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < data.Length; i++)
        {
            if (i > 0 && i % _format.BytesPerLine == 0)
                sb.AppendLine();

            if (i > 0 && i % _format.BytesPerLine != 0)
                sb.Append(' ');

            sb.Append(_format.UseHex ? $"0x{data[i]:X2}" : data[i].ToString());

            if (i < data.Length - 1)
                sb.Append(',');
        }
        return sb.ToString();
    }
}
