using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;

namespace TOfont.WinUI.Services;

/// <summary>
/// CLI 模式的串口服务 — 独立于界面页面的 SerialPort 实例。
/// GUI 与 CLI 各自管理自己的串口，同时占用同一端口会返回错误。
/// 接收数据放入线程安全队列，供 HTTP API 读取。
/// </summary>
public sealed class SerialPortCliService : IDisposable
{
    private readonly SerialPort _port = new();
    private readonly ConcurrentQueue<byte> _rxQueue = new();
    private bool _disposed;

    /// <summary>接收数据事件（整块字节），供 SSE 实时推送订阅。</summary>
    public event Action<byte[]>? DataReceivedBlock;

    public bool IsOpen => _port.IsOpen;

    public string? PortName => _port.PortName;
    public int BaudRate => _port.BaudRate;
    public int DataBits => _port.DataBits;
    public StopBits StopBits => _port.StopBits;
    public Parity Parity => _port.Parity;

    public SerialPortCliService()
    {
        _port.DataReceived += OnDataReceived;
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (!_port.IsOpen) return;
        try
        {
            var count = _port.BytesToRead;
            var data = new byte[count];
            var read = _port.Read(data, 0, count);
            for (var i = 0; i < read; i++)
                _rxQueue.Enqueue(data[i]);
            // 触发 SSE 推送（Channel.TryWrite 线程安全，不会影响串口线程）
            DataReceivedBlock?.Invoke(data[..read]);
        }
        catch { /* 端口关闭竞态，忽略 */ }
    }

    public static string[] ListPorts()
    {
        try { return SerialPort.GetPortNames(); }
        catch { return []; }
    }

    public string Open(string port, int baud, int dataBits, string stopBits, string parity)
    {
        if (string.IsNullOrWhiteSpace(port))
            return "端口名不能为空";

        try
        {
            _port.PortName = port.Trim();
            _port.BaudRate = baud > 0 ? baud : 115200;
            _port.DataBits = dataBits is 5 or 6 or 7 or 8 ? dataBits : 8;
            _port.StopBits = stopBits?.ToLowerInvariant() switch
            {
                "1.5" or "onepointfive" => StopBits.OnePointFive,
                "2" or "two" => StopBits.Two,
                _ => StopBits.One
            };
            _port.Parity = parity?.ToLowerInvariant() switch
            {
                "odd" => Parity.Odd,
                "even" => Parity.Even,
                "mark" => Parity.Mark,
                "space" => Parity.Space,
                _ => Parity.None
            };
            _port.Open();
            return "";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public string Close()
    {
        try
        {
            _port.DataReceived -= OnDataReceived;
            if (_port.IsOpen) _port.Close();
            return "";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>发送字节。返回空串表示成功，否则返回错误信息。</summary>
    public string Send(byte[] data)
    {
        if (!_port.IsOpen) return "串口未打开";
        try
        {
            _port.Write(data, 0, data.Length);
            return "";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>取走并清空接收队列。</summary>
    public byte[] DrainReceived()
    {
        var list = new List<byte>(_rxQueue.Count);
        while (_rxQueue.TryDequeue(out var b))
            list.Add(b);
        return list.ToArray();
    }

    /// <summary>按编码把字节解码为文本。gbk 用系统代码页 936。</summary>
    public static string BytesToText(byte[] bytes, string encoding)
    {
        if (bytes.Length == 0) return "";
        try
        {
            var enc = encoding?.ToLowerInvariant() switch
            {
                "utf-8" or "utf8" => Encoding.UTF8,
                _ => Encoding.GetEncoding(936)
            };
            return enc.GetString(bytes);
        }
        catch
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    /// <summary>把文本编码为字节。默认 GBK，与串口助手界面默认一致。</summary>
    public static byte[] TextToBytes(string text, string encoding)
    {
        try
        {
            var enc = encoding?.ToLowerInvariant() switch
            {
                "utf-8" or "utf8" => Encoding.UTF8,
                _ => Encoding.GetEncoding(936)
            };
            return enc.GetBytes(text);
        }
        catch
        {
            return Encoding.UTF8.GetBytes(text);
        }
    }

    /// <summary>解析 HEX 字符串（忽略空格/逗号/0x 前缀等非法字符）。</summary>
    public static byte[] HexToBytes(string hex)
    {
        var clean = new StringBuilder(hex.Length);
        foreach (var c in hex)
        {
            if (Uri.IsHexDigit(c)) clean.Append(c);
        }
        var s = clean.ToString();
        if (s.Length == 0) return [];
        if (s.Length % 2 == 1) s = "0" + s; // 奇数位前面补 0
        var bytes = new byte[s.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
        return bytes;
    }

    public static string BytesToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 3);
        foreach (var b in bytes)
            sb.Append(b.ToString("X2")).Append(' ');
        return sb.ToString().TrimEnd();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Close(); } catch { }
        _port.Dispose();
    }
}
