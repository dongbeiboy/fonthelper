namespace TOfont.Core.Models;

/// <summary>
/// 字形信息 — 单个字符的取模结果
/// </summary>
public class GlyphInfo
{
    public char Character { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[] DotData { get; set; } = [];
    public int ByteWidth => (Width + 7) / 8;
}
