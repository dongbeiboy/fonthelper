using System.Text;

namespace TOfont.WinUI.Tools.SerialPortTool;

/// <summary>
/// 简易终端仿真器 — 处理串口控制字符，把原始字节流变成"像终端"的文本：
///   - 0x08 退格：删除光标前一个字符
///   - 回车单独出现（无换行）：回到当前行首，后续文本覆盖该行（U-Boot 倒计时等）
///   - ANSI 转义 \x1b[...：解析并忽略（清屏/光标移动/颜色），不显示乱码
/// 状态跨数据包保持（退格可作用于之前分包收到的文本）。
///
/// 增量输出约定：
///   <see cref="Feed"/> 返回 true 表示本次发生"改写历史"（退格/回车覆盖/清屏/裁剪），
///   调用方必须用 <see cref="Text"/> 全量重设 UI；
///   返回 false 表示纯追加，调用方只需 <see cref="TakeAppend"/> 取新增部分追加，
///   避免高频数据流反复全量重设 TextBox。
/// </summary>
public sealed class TerminalEmulator
{
    private const int MaxChars = 50000;
    private readonly StringBuilder _sb = new();

    /// <summary>ANSI 序列可能跨数据包，未完整时暂存</summary>
    private string _pendingEsc = "";

    /// <summary>上次已同步到 UI 的字符位置（增量追加的起点）</summary>
    private int _flushed;

    /// <summary>本次 Feed 是否发生改写（需要全量刷新）</summary>
    private bool _dirty;

    /// <summary>当前累积的完整终端文本（上限 50k 字符）。</summary>
    public string Text => _sb.ToString();

    public void Clear()
    {
        _sb.Clear();
        _pendingEsc = "";
        _flushed = 0;
        _dirty = false;
    }

    /// <summary>
    /// 输入一段解码后的文本（可能含控制字符），更新内部状态。
    /// </summary>
    /// <returns>true=发生改写需全量刷新；false=纯追加，用 <see cref="TakeAppend"/> 取增量。</returns>
    public bool Feed(string input)
    {
        if (input.Length == 0) return false;

        _dirty = false;

        // 拼接上一个包残留的未完成转义序列
        var data = _pendingEsc + input;
        _pendingEsc = "";

        var lineStart = 0;          // 当前行起始（用于回车覆盖）
        var lineEnd = _sb.Length;   // 当前行文本结束
        var i = 0;

        while (i < data.Length)
        {
            var c = data[i];

            // ANSI 转义序列：ESC [ ... 终止字节（0x40~0x7E）
            if (c == '\x1b')
            {
                var j = i + 1;
                if (j >= data.Length) { _pendingEsc = data[i..]; break; }
                if (data[j] == '[')
                {
                    var k = j + 1;
                    while (k < data.Length && !(data[k] >= 0x40 && data[k] <= 0x7E))
                        k++;
                    if (k >= data.Length) { _pendingEsc = data[i..]; break; }
                    var seq = data[(i + 2)..k];
                    if (seq is "2J" or "3J" or "1J")
                    {
                        // 清屏：整个清空
                        _sb.Clear();
                        lineStart = lineEnd = 0;
                        _dirty = true;
                    }
                    else if (seq == "K")
                    {
                        // 清当前行光标后：截断到当前行起始
                        if (lineEnd < _sb.Length) _sb.Length = lineEnd;
                        _dirty = true;
                    }
                    // 其他（光标移动/颜色等）直接忽略
                    i = k + 1;
                }
                else
                {
                    // 非 CSI 的 ESC 序列，忽略
                    i = j + 1;
                }
                continue;
            }

            switch (c)
            {
                case '\b': // 退格：删当前行最后一个字符
                    if (lineEnd > lineStart)
                    {
                        lineEnd--;
                        _sb.Length = lineEnd;
                        _dirty = true;
                    }
                    i++;
                    break;

                case '\r': // 回车：回当前行首，后续覆盖（CRLF 视为换行）
                    i++;
                    if (i < data.Length && data[i] == '\n')
                    {
                        _sb.Append('\n');
                        lineStart = lineEnd = _sb.Length;
                        i++;
                    }
                    else
                    {
                        lineEnd = lineStart;
                        _dirty = true;
                    }
                    break;

                case '\n': // 换行：开新行
                    _sb.Append('\n');
                    lineStart = lineEnd = _sb.Length;
                    i++;
                    break;

                case '\t':
                    _sb.Append("    ");
                    lineEnd = _sb.Length;
                    i++;
                    break;

                default:
                    if (c >= 0x20 && c != 0x7F) // 可打印（DEL 忽略）
                    {
                        _sb.Append(c);
                        lineEnd = _sb.Length;
                    }
                    i++;
                    break;
            }
        }

        // 超过上限时裁剪（保留结尾，终端习惯保留最新）
        if (_sb.Length > MaxChars)
        {
            var remove = _sb.Length - MaxChars;
            _sb.Remove(0, remove);
            var nl = _sb.ToString().IndexOf('\n');
            if (nl > 0) _sb.Remove(0, nl + 1);
            lineStart = lineEnd = _sb.Length;
            _dirty = true;
        }

        if (_dirty)
        {
            _flushed = _sb.Length;
            _dirty = false;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 上次 <see cref="Feed"/> 返回 false 后，取本次纯追加的新增文本，并推进同步位置。
    /// </summary>
    public string TakeAppend()
    {
        if (_flushed >= _sb.Length) return "";
        var delta = _sb.ToString(_flushed, _sb.Length - _flushed);
        _flushed = _sb.Length;
        return delta;
    }
}
