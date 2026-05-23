using System.Text;

namespace TOfont.Core.Parsing;

/// <summary>
/// PTL 文件读取器 — 解析 PCtoLCD 的字符列表文件。
/// 支持 ASC.PTL（单字节 ASCII）和 Gb2312.PTL（双字节 GB2312 编码）。
/// </summary>
public class PtlReader
{
    /// <summary>
    /// 读取 PTL 文件并返回字符列表。
    /// 自动检测编码：如果文件不含 0x0D 0x0A 分隔符，则按纯文本行读取；
    /// 否则按二进制 GB2312 双字节格式解析。
    /// </summary>
    public string[] Read(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);

        // 检测是否为二进制 GB2312 格式（含 0x0D 0x0A 分隔符的字节流）
        var hasCrLf = false;
        for (var i = 0; i < bytes.Length - 1; i++)
        {
            if (bytes[i] == 0x0D && bytes[i + 1] == 0x0A)
            {
                hasCrLf = true;
                break;
            }
        }

        if (hasCrLf)
            return ReadGb2312(bytes);

        // 纯文本行模式（ASC.PTL 等）
        var lines = File.ReadAllLines(filePath, Encoding.Default);
        return ParseLines(lines);
    }

    /// <summary>
    /// 解析 GB2312 双字节编码的 PTL 文件。
    /// 格式：字节流中每 2 字节为一组 GB2312 字符，以 0D 0A 分隔。
    /// </summary>
    private static string[] ReadGb2312(byte[] bytes)
    {
        var gb2312 = Encoding.GetEncoding("gb2312");
        var result = new List<string>();
        var buffer = new List<byte>();

        for (var i = 0; i < bytes.Length; i++)
        {
            // 跳过 0D 0A 分隔符
            if (i + 1 < bytes.Length && bytes[i] == 0x0D && bytes[i + 1] == 0x0A)
            {
                // 将缓冲区中的字节解码为 GB2312 字符
                if (buffer.Count >= 2)
                {
                    var ch = gb2312.GetString(buffer.ToArray());
                    if (!string.IsNullOrWhiteSpace(ch))
                        result.Add(ch);
                }
                buffer.Clear();
                i++; // 跳过 0x0A
                continue;
            }

            buffer.Add(bytes[i]);
        }

        // 处理最后一组
        if (buffer.Count >= 2)
        {
            var ch = gb2312.GetString(buffer.ToArray());
            if (!string.IsNullOrWhiteSpace(ch))
                result.Add(ch);
        }

        return result.ToArray();
    }

    /// <summary>
    /// 解析纯文本 PTL 内容行
    /// </summary>
    public string[] ParseLines(string[] lines)
    {
        var result = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            result.Add(trimmed);
        }

        return result.ToArray();
    }
}
