namespace TOfont.Core.Models;

/// <summary>
/// 输出格式配置
/// </summary>
public class OutputFormat
{
    public string Prefix { get; set; } = "unsigned char";
    public string Suffix { get; set; } = ";";
    public string Comment { get; set; } = "//";
    public bool UseHex { get; set; } = true;
    public string LineTemplate { get; set; } = "{prefix} {name}[] = {{{data}}}{suffix}";
    public int BytesPerLine { get; set; } = 16;
}
