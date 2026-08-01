using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using Windows.System;

namespace TOfont.WinUI.Tools.SerialPortTool;

/// <summary>
/// 串口助手 — 参考 sucaibao/串口助手 (WinForms V1.1) 重写的 WinUI 3 版本。
/// 功能对齐原版：串口配置、HEX/文本收发、GBK/UTF-8 编码、多字节分包缓冲。
/// 内置 Shell 终端（类 PuTTY）：行模式（命令历史）/ 字符直通，仅远端回显。
/// </summary>
public sealed partial class SerialPortPage : Page
{
    private readonly SerialPort _serialPort = new();
    private bool _isOpen;

    // 接收字节缓冲 — 多字节字符分包时暂存不完整字节，跨数据包合并
    private readonly List<byte> _receiveBuffer = new();

    // Shell 终端状态
    private bool _shellPassthrough;         // 字符直通模式
    private bool _clearingShellInput;       // 防止清空输入框时递归触发
    private string _lastShellInputText = "";
    private readonly List<string> _shellHistory = new();
    private int _shellHistoryIndex = -1;
    private string _shellHistoryDraft = "";
    private int _activeTabIndex;            // 0=调试助手, 1=Shell 终端

    public SerialPortPage()
    {
        InitializeComponent();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        BaudCombo.SelectedIndex = 4;        // 115200
        DataBitsCombo.SelectedIndex = 0;    // 8
        StopBitsCombo.SelectedIndex = 0;    // 1
        ParityCombo.SelectedIndex = 0;      // 无

        RefreshPorts();

        // 断开时自动复位界面（USB 拔出等）
        _serialPort.PinChanged += OnPinChanged;
    }

    private void RefreshPorts()
    {
        var current = PortCombo.SelectedItem?.ToString() ?? "";
        PortCombo.Items.Clear();
        foreach (var name in SerialPort.GetPortNames())
            PortCombo.Items.Add(name);
        if (PortCombo.Items.Count > 0)
        {
            PortCombo.SelectedIndex = PortCombo.Items.IndexOf(current);
            if (PortCombo.SelectedIndex < 0) PortCombo.SelectedIndex = 0;
        }
    }

    private void OnPortDropDown(object sender, object e) => RefreshPorts();

    private void OnRefreshPorts(object sender, RoutedEventArgs e) => RefreshPorts();

    private void OnTogglePort(object sender, RoutedEventArgs e)
    {
        if (_isOpen) ClosePort();
        else OpenPort();
    }

    private void OpenPort()
    {
        try
        {
            _serialPort.PortName = PortCombo.SelectedItem?.ToString() ?? "";
            _serialPort.BaudRate = int.Parse((BaudCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "115200");
            _serialPort.DataBits = int.Parse((DataBitsCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "8");
            _serialPort.StopBits = StopBitsCombo.SelectedIndex switch
            {
                1 => StopBits.OnePointFive,
                2 => StopBits.Two,
                _ => StopBits.One
            };
            _serialPort.Parity = ParityCombo.SelectedIndex switch
            {
                1 => Parity.Odd,
                2 => Parity.Even,
                _ => Parity.None
            };

            _serialPort.DataReceived -= OnDataReceived;
            _serialPort.DataReceived += OnDataReceived;
            _serialPort.Open();
            _isOpen = true;

            OpenBtn.Content = "关闭串口";
            PortStatus.Text = $"已连接: {_serialPort.PortName} @ {_serialPort.BaudRate}";
            StatusBar.Text = $"串口 {_serialPort.PortName} 已打开";
            SetConfigEnabled(false);
        }
        catch (Exception ex)
        {
            StatusBar.Text = $"打开失败: {ex.Message}";
        }
    }

    private void ClosePort()
    {
        try
        {
            _serialPort.DataReceived -= OnDataReceived;
            if (_serialPort.IsOpen) _serialPort.Close();
        }
        catch { }
        _isOpen = false;
        OpenBtn.Content = "打开串口";
        PortStatus.Text = "未连接";
        StatusBar.Text = "串口已关闭";
        SetConfigEnabled(true);
    }

    /// <summary>
    /// USB 拔出等引脚变化时，若串口已不可用则自动关闭复位
    /// </summary>
    private void OnPinChanged(object sender, SerialPinChangedEventArgs e)
    {
        if (_isOpen && !_serialPort.IsOpen)
        {
            DispatcherQueue.TryEnqueue(ClosePort);
        }
    }

    private void SetConfigEnabled(bool enabled)
    {
        PortCombo.IsEnabled = enabled;
        BaudCombo.IsEnabled = enabled;
        DataBitsCombo.IsEnabled = enabled;
        StopBitsCombo.IsEnabled = enabled;
        ParityCombo.IsEnabled = enabled;
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (!_serialPort.IsOpen) return;

        var count = _serialPort.BytesToRead;
        var data = new byte[count];
        var read = _serialPort.Read(data, 0, count);
        if (read <= 0) return;

        // 回 UI 线程追加显示
        DispatcherQueue.TryEnqueue(() =>
        {
            AppendReceive(data, read);
        });
    }

    private void AppendReceive(byte[] data, int count)
    {
        if (_activeTabIndex == 1) // Shell 终端：原始字节直接解码显示（仅远端回显）
        {
            var encoding = (ShellCodingCombo.SelectedIndex == 1) ? "utf-8" : "gb2312";
            var text = Encoding.GetEncoding(encoding).GetString(data, 0, count);
            ShellOutputBox.Text += text;
            if (ShellOutputBox.Text.Length > 50000)
                ShellOutputBox.Text = ShellOutputBox.Text[^25000..];
            // 自动滚动到底部
            ShellOutputBox.SelectionStart = ShellOutputBox.Text.Length;
            ShellOutputBox.SelectionLength = 0;
        }
        else if (ReceiveModeCombo.SelectedIndex == 0) // HEX
        {
            ReceiveBox.Text += BytesToHex(data, count);
        }
        else // 文本
        {
            ReceiveBox.Text += BytesToText(data, count);
        }
        if (ReceiveBox.Text.Length > 50000)
            ReceiveBox.Text = ReceiveBox.Text[^25000..];
    }

    // ========== 字节流转换（抽取自原版） ==========

    /// <summary>
    /// 字节流转文本 — 处理多字节字符分包：不完整的字节暂存缓冲，下个数据包到达时合并解码。
    /// 原版 GBK 按 1/2 字节、UTF-8 按 1~4 字节判定并跨包合并。
    /// </summary>
    private string BytesToText(byte[] bytes, int count)
    {
        _receiveBuffer.AddRange(bytes.Take(count));

        var decode = new List<byte>();
        var encoding = (ReceiveCodingCombo.SelectedIndex == 1) ? "utf-8" : "gb2312";

        while (_receiveBuffer.Count > 0)
        {
            var b = _receiveBuffer[0];
            int charLen;

            if (b < 0x80) charLen = 1;                       // 单字节
            else if ((b & 0xE0) == 0xC0) charLen = 2;         // 2 字节
            else if ((b & 0xF0) == 0xE0) charLen = 3;         // 3 字节
            else if ((b & 0xF8) == 0xF0) charLen = 4;         // 4 字节
            else charLen = 1;                                 // 其他按单字节

            if (_receiveBuffer.Count < charLen)
                break; // 字节不完整，等下一个数据包

            decode.AddRange(_receiveBuffer.Take(charLen));
            _receiveBuffer.RemoveRange(0, charLen);
        }

        return Encoding.GetEncoding(encoding).GetString(decode.ToArray());
    }

    private static string BytesToHex(byte[] bytes, int count)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
            sb.Append(bytes[i].ToString("X2")).Append(' ');
        return sb.ToString();
    }

    private byte[] TextToBytes(string str)
    {
        var encoding = (SendCodingCombo.SelectedIndex == 1) ? "utf-8" : "gb2312";
        return Encoding.GetEncoding(encoding).GetBytes(str);
    }

    private static byte[] HexToBytes(string str)
    {
        var hex = Regex.Replace(str, "[^A-Fa-f0-9]", ""); // 清除非法字符
        if (hex.Length == 0) return [];

        var count = (hex.Length + 1) / 2;
        var bytes = new byte[count];
        for (var i = 0; i < count; i++)
        {
            var chunk = hex.Length >= (i + 1) * 2
                ? hex.Substring(i * 2, 2)
                : hex.Substring(i * 2, 1);
            bytes[i] = byte.Parse(chunk, System.Globalization.NumberStyles.HexNumber);
        }
        return bytes;
    }

    // ========== 事件 ==========

    private void OnSend(object sender, RoutedEventArgs e)
    {
        if (!_serialPort.IsOpen)
        {
            StatusBar.Text = "请先打开串口";
            return;
        }

        var text = SendBox.Text;
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            var data = SendModeCombo.SelectedIndex == 0 ? HexToBytes(text) : TextToBytes(text);
            if (data.Length == 0) return;
            _serialPort.Write(data, 0, data.Length);
            StatusBar.Text = $"已发送 {data.Length} 字节";
        }
        catch (Exception ex)
        {
            StatusBar.Text = $"发送失败: {ex.Message}";
        }
    }

    private void OnClearReceive(object sender, RoutedEventArgs e)
    {
        ReceiveBox.Text = "";
        _receiveBuffer.Clear();
    }

    private void OnClearSend(object sender, RoutedEventArgs e) => SendBox.Text = "";

    private void OnReceiveModeChanged(object sender, SelectionChangedEventArgs e)
    {
        // 切换模式时清掉不完整的编码缓冲，避免串码
        _receiveBuffer.Clear();
        ReceiveCodingCombo.IsEnabled = ReceiveModeCombo.SelectedIndex == 1;
    }

    private void OnSendModeChanged(object sender, SelectionChangedEventArgs e)
    {
        SendCodingCombo.IsEnabled = SendModeCombo.SelectedIndex == 1;
    }

    // ========== Shell 终端（类 PuTTY） ==========

    /// <summary>
    /// Tab 切换 — 记录当前活动 tab，接收数据按此路由
    /// </summary>
    private void OnModeTabChanged(object sender, SelectionChangedEventArgs e)
    {
        _activeTabIndex = ModePivot.SelectedIndex;
        if (_activeTabIndex == 1) ShellInputBox.Focus(FocusState.Programmatic);
    }

    private void OnShellModeChanged(object sender, SelectionChangedEventArgs e)
    {
        _shellPassthrough = ShellModeCombo.SelectedIndex == 1;
        _lastShellInputText = "";
        _shellHistoryIndex = -1;
        _clearingShellInput = true;
        ShellInputBox.Text = "";
        _clearingShellInput = false;
        ShellInputBox.PlaceholderText = _shellPassthrough
            ? "直通模式：输入即发送（仅远端回显）"
            : "输入命令，Enter 发送（↑↓ 历史）";
    }

    private void OnShellInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_shellPassthrough) return;

        if (e.Key == VirtualKey.Enter)
        {
            SendShellLine();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Up)
        {
            if (_shellHistory.Count > 0)
            {
                if (_shellHistoryIndex == -1)
                    _shellHistoryDraft = ShellInputBox.Text;
                _shellHistoryIndex = Math.Min(_shellHistoryIndex + 1, _shellHistory.Count - 1);
                ShellInputBox.Text = _shellHistory[_shellHistory.Count - 1 - _shellHistoryIndex];
                ShellInputBox.SelectionStart = ShellInputBox.Text.Length;
            }
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Down)
        {
            if (_shellHistoryIndex >= 0)
            {
                _shellHistoryIndex--;
                ShellInputBox.Text = _shellHistoryIndex >= 0
                    ? _shellHistory[_shellHistory.Count - 1 - _shellHistoryIndex]
                    : _shellHistoryDraft;
                ShellInputBox.SelectionStart = ShellInputBox.Text.Length;
            }
            e.Handled = true;
        }
    }

    /// <summary>
    /// 字符直通模式：输入即发送，发送后立即清空输入框
    /// </summary>
    private void OnShellInputChanged(object sender, TextChangedEventArgs e)
    {
        if (!_shellPassthrough || _clearingShellInput) return;

        var text = ShellInputBox.Text;
        if (text.Length > _lastShellInputText.Length)
        {
            var added = text[_lastShellInputText.Length..];
            SendShellBytes(added);
        }
        _lastShellInputText = text;

        // 输入即发即清
        _clearingShellInput = true;
        ShellInputBox.Text = "";
        _clearingShellInput = false;
        _lastShellInputText = "";
    }

    private void OnShellSend(object sender, RoutedEventArgs e) => SendShellLine();

    /// <summary>
    /// 行模式发送：整行 + 回车（嵌入式 CLI 普遍识别 CR）
    /// </summary>
    private void SendShellLine()
    {
        var line = ShellInputBox.Text;
        if (string.IsNullOrEmpty(line)) return;

        SendShellBytes(line + "\r");

        if (_shellHistory.Count == 0 || _shellHistory[^1] != line)
            _shellHistory.Add(line);
        if (_shellHistory.Count > 50) _shellHistory.RemoveAt(0);
        _shellHistoryIndex = -1;

        _clearingShellInput = true;
        ShellInputBox.Text = "";
        _clearingShellInput = false;
        _lastShellInputText = "";
    }

    private void SendShellBytes(string text)
    {
        if (!_serialPort.IsOpen)
        {
            StatusBar.Text = "请先打开串口";
            return;
        }
        try
        {
            var encoding = (ShellCodingCombo.SelectedIndex == 1) ? "utf-8" : "gb2312";
            var data = Encoding.GetEncoding(encoding).GetBytes(text);
            _serialPort.Write(data, 0, data.Length);
        }
        catch (Exception ex)
        {
            StatusBar.Text = $"发送失败: {ex.Message}";
        }
    }

    private void OnClearShell(object sender, RoutedEventArgs e) => ShellOutputBox.Text = "";
}
