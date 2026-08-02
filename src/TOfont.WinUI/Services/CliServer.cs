using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TOfont.WinUI.Services;

/// <summary>
/// CLI 模式 HTTP 服务 — 通过 TcpListener 手写极简 HTTP 服务器，监听 127.0.0.1。
/// 让 agent 工具通过 REST API 远程使用串口助手。
///
/// API：
///   GET  /api/health              → 服务健康检查
///   GET  /api/ports               → 串口列表
///   GET  /api/status              → 当前串口状态
///   POST /api/open                → 打开串口 {port, baud, dataBits, stopBits, parity}
///   POST /api/close               → 关闭串口
///   POST /api/send                → 发送 {data, mode:"hex"|"text", encoding:"gbk"|"utf-8"}
///   GET  /api/read?format=hex     → 读取并清空接收数据 {data, bytes}
/// </summary>
public sealed class CliServer : IDisposable
{
    private readonly SerialPortCliService _service = new();
    private readonly SemaphoreSlim _gate = new(1, 1); // 串口单客户端，请求串行处理
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public int Port { get; }
    public bool IsRunning { get; private set; }

    public CliServer(int port)
    {
        Port = port;
    }

    /// <summary>启动服务。返回空串表示成功，否则返回错误信息。</summary>
    public string Start()
    {
        lock (this)
        {
            if (IsRunning) return "";
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, Port);
                _listener.Start();
                _cts = new CancellationTokenSource();
                IsRunning = true;
                _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
                return "";
            }
            catch (Exception ex)
            {
                Stop();
                return ex.Message;
            }
        }
    }

    public void Stop()
    {
        lock (this)
        {
            if (!IsRunning) return;
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }
            _listener = null;
            _cts = null;
            IsRunning = false;
            try { _service.Close(); } catch { }
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }
            catch { break; }

            _ = Task.Run(async () =>
            {
                try { await HandleClientAsync(client); }
                catch { }
                finally { client.Dispose(); }
            }, CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using var stream = client.GetStream();
        stream.ReadTimeout = 5000;
        stream.WriteTimeout = 5000;

        // 读取请求头（直到空行），限制大小
        var headerBuf = new byte[8192];
        var headerBytes = new List<byte>(1024);
        var total = 0;
        var headerEnd = -1;
        while (total < headerBuf.Length)
        {
            var n = await stream.ReadAsync(headerBuf.AsMemory(total, headerBuf.Length - total));
            if (n <= 0) return;
            total += n;
            headerBytes.AddRange(headerBuf.Take(n));
            // 查找 \r\n\r\n
            for (var i = Math.Max(0, total - 4); i < total - 3; i++)
            {
                if (headerBytes[i] == 13 && headerBytes[i + 1] == 10 &&
                    headerBytes[i + 2] == 13 && headerBytes[i + 3] == 10)
                {
                    headerEnd = i + 4;
                    break;
                }
            }
            if (headerEnd >= 0) break;
        }
        if (headerEnd < 0) return;

        var headerText = Encoding.ASCII.GetString(headerBytes.ToArray(), 0, headerEnd);
        var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return;
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2) return;
        var method = requestLine[0].ToUpperInvariant();
        var target = requestLine[1];

        // 解析 Content-Length
        var contentLength = 0;
        foreach (var line in lines.Skip(1))
        {
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            var name = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                int.TryParse(value, out contentLength);
        }

        // 读 body
        byte[]? body = null;
        if (contentLength > 0 && contentLength <= 1024 * 1024)
        {
            body = new byte[contentLength];
            var read = 0;
            // 可能部分 body 已在 headerBuf 中
            var leftover = total - headerEnd;
            if (leftover > 0)
            {
                var copy = Math.Min(leftover, contentLength);
                Array.Copy(headerBytes.ToArray(), headerEnd, body, 0, copy);
                read = copy;
            }
            while (read < contentLength)
            {
                var n = await stream.ReadAsync(body.AsMemory(read, contentLength - read));
                if (n <= 0) break;
                read += n;
            }
        }

        // CORS 预检
        if (method == "OPTIONS")
        {
            await WriteResponseAsync(stream, 204, "", allowCors: true);
            return;
        }

        // 串行处理，避免并发操作同一串口
        await _gate.WaitAsync();
        string payload;
        int statusCode;
        try
        {
            (statusCode, payload) = Route(method, target, body);
        }
        finally
        {
            _gate.Release();
        }

        await WriteResponseAsync(stream, statusCode, payload, allowCors: true);
    }

    private (int Code, string Payload) Route(string method, string target, byte[]? body)
    {
        var path = target.Split('?')[0];
        var query = target.Contains('?') ? target[(target.IndexOf('?') + 1)..] : "";

        switch (path)
        {
            case "/api/health":
                return (200, Json(new { status = "ok", port = Port }));

            case "/api/ports":
                return (200, Json(new { ports = SerialPortCliService.ListPorts() }));

            case "/api/status":
                return (200, Json(new
                {
                    open = _service.IsOpen,
                    port = _service.IsOpen ? _service.PortName : null,
                    baud = _service.IsOpen ? _service.BaudRate : 0,
                    dataBits = _service.IsOpen ? _service.DataBits : 0,
                    stopBits = _service.IsOpen ? _service.StopBits.ToString() : null,
                    parity = _service.IsOpen ? _service.Parity.ToString() : null
                }));

            case "/api/open" when method == "POST":
            {
                var d = ParseBody(body);
                var error = _service.Open(
                    GetString(d, "port") ?? "",
                    GetInt(d, "baud", 115200),
                    GetInt(d, "dataBits", 8),
                    GetString(d, "stopBits") ?? "1",
                    GetString(d, "parity") ?? "None");
                if (error.Length > 0)
                    return (400, Json(new { ok = false, error }));
                return (200, Json(new { ok = true }));
            }

            case "/api/close" when method == "POST":
            {
                var error = _service.Close();
                if (error.Length > 0)
                    return (400, Json(new { ok = false, error }));
                return (200, Json(new { ok = true }));
            }

            case "/api/send" when method == "POST":
            {
                var d = ParseBody(body);
                var data = GetString(d, "data") ?? "";
                var mode = (GetString(d, "mode") ?? "hex").ToLowerInvariant();
                var encoding = GetString(d, "encoding");

                byte[] bytes;
                if (mode == "text")
                    bytes = SerialPortCliService.TextToBytes(data, encoding ?? "");
                else
                    bytes = SerialPortCliService.HexToBytes(data);

                var error = _service.Send(bytes);
                if (error.Length > 0)
                    return (400, Json(new { ok = false, error }));
                return (200, Json(new { ok = true, bytes = bytes.Length }));
            }

            case "/api/read":
            {
                var received = _service.DrainReceived();
                var format = GetQueryValue(query, "format");
                if (format is not ("hex" or "text")) format = "hex";
                string data;
                if (format == "text")
                    data = SerialPortCliService.BytesToText(received, GetQueryValue(query, "encoding"));
                else
                    data = SerialPortCliService.BytesToHex(received);
                return (200, Json(new { bytes = received.Length, format, data }));
            }

            default:
                return (404, Json(new { ok = false, error = $"未找到: {method} {path}" }));
        }
    }

    private static JsonElement? ParseBody(byte[]? body)
    {
        if (body == null || body.Length == 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static string? GetString(JsonElement? d, string name)
    {
        if (d is not { } e || !e.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String)
            return null;
        return p.GetString();
    }

    private static int GetInt(JsonElement? d, string name, int def)
    {
        if (d is not { } e || !e.TryGetProperty(name, out var p))
            return def;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v))
            return v;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var s))
            return s;
        return def;
    }

    private static string Json(object obj) => JsonSerializer.Serialize(obj);

    private static string GetQueryValue(string query, string name)
    {
        foreach (var part in query.Split('&'))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && kv[0].Equals(name, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(kv[1]);
        }
        return "";
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string body, bool allowCors)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header = new StringBuilder()
            .Append("HTTP/1.1 ").Append(status).Append(' ').Append(StatusText(status)).Append("\r\n")
            .Append("Content-Type: application/json; charset=utf-8\r\n")
            .Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n")
            .Append("Connection: close\r\n");
        if (allowCors)
            header.Append("Access-Control-Allow-Origin: *\r\n")
                  .Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n")
                  .Append("Access-Control-Allow-Headers: Content-Type\r\n");
        header.Append("\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(header.ToString());
        await stream.WriteAsync(headerBytes);
        if (bodyBytes.Length > 0)
            await stream.WriteAsync(bodyBytes);
        await stream.FlushAsync();
    }

    private static string StatusText(int code) => code switch
    {
        200 => "OK",
        204 => "No Content",
        400 => "Bad Request",
        404 => "Not Found",
        500 => "Internal Server Error",
        _ => "OK"
    };

    public void Dispose()
    {
        Stop();
        _service.Dispose();
        _gate.Dispose();
    }
}
