using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using Windows.System;

namespace TOfont.WinUI.Tools.SerialPortTool;

/// <summary>
/// 串口助手 — 参考 sucaibao/串口助手 (WinForms V1.1) 重写的 WinUI 3 版本。
/// 功能对齐原版：串口配置、HEX/文本收发、GBK/UTF-8 编码、多字节分包缓冲。
/// 内置 Shell 终端：仿真终端模式（退格/回车/ANSI 解析 + 命令历史）/ 透传模式。
/// </summary>
public sealed partial class SerialPortPage : Page
{
    private readonly SerialPort _serialPort = new();
    private bool _isOpen;

    // 接收字节缓冲 — 多字节字符分包时暂存不完整字节，跨数据包合并
    private readonly List<byte> _receiveBuffer = new();

    // 接收节流 — 高频小包先累积，定时批量刷新 UI，避免仿真模式全量重设 TextBox 卡顿
    private readonly object _rxLock = new();
    private readonly List<byte> _pendingRx = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _rxTimer;
    private bool _rxTimerRunning;

    // Shell 终端状态
    private readonly TerminalEmulator _emulator = new();
    private bool _shellPassthrough;         // true = 透传模式（原始字节原样显示）
    private bool _shellExpanded;            // true = 输入框多行展开态
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

        // 接收节流定时器：50ms 批量刷新一次
        _rxTimer = DispatcherQueue.CreateTimer();
        _rxTimer.Interval = TimeSpan.FromMilliseconds(50);
        _rxTimer.Tick += (_, _) => FlushPendingRx();

        RefreshPorts();

        // 断开时自动复位界面（USB 拔出等）
        _serialPort.PinChanged += OnPinChanged;
    }

    private void RefreshPorts()
    {
        try
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
        catch
        {
            // 枚举串口失败（注册表受限等）不影响页面使用
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

        // 先入缓冲，定时批量刷新（串口回调在后台线程，不能直接碰 UI）
        lock (_rxLock)
        {
            for (var i = 0; i < read; i++) _pendingRx.Add(data[i]);
            if (!_rxTimerRunning && _rxTimer != null)
            {
                _rxTimerRunning = true;
                _rxTimer.Start();
            }
        }
    }

    /// <summary>定时器触发：取走累积字节，一次性更新 UI。</summary>
    private void FlushPendingRx()
    {
        byte[] batch;
        lock (_rxLock)
        {
            _rxTimerRunning = false;
            _rxTimer?.Stop();
            batch = _pendingRx.ToArray();
            _pendingRx.Clear();
        }
        if (batch.Length == 0) return;
        AppendReceive(batch, batch.Length);
    }

    private void AppendReceive(byte[] data, int count)
    {
        if (_activeTabIndex == 1) // Shell 终端：原始字节直接解码显示
        {
            var encoding = SelectedCoding(ShellCodingCombo);
            var text = encoding.GetString(data, 0, count);
            AppendShellOutput(text);
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
        var encoding = SelectedCoding(ReceiveCodingCombo);

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

        return encoding.GetString(decode.ToArray());
    }

    private static string BytesToHex(byte[] bytes, int count)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
            sb.Append(bytes[i].ToString("X2")).Append(' ');
        return sb.ToString();
    }

    /// <summary>根据编码下拉框选中的项名返回对应 Encoding。默认 GBK。</summary>
    private static Encoding SelectedCoding(ComboBox combo)
    {
        var name = (combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "GBK";
        try
        {
            return name switch
            {
                "UTF-8" => Encoding.UTF8,
                "ASCII" => Encoding.ASCII,
                "GB2312" => Encoding.GetEncoding("gb2312"),
                "UTF-16LE" => Encoding.Unicode,
                "UTF-16BE" => Encoding.BigEndianUnicode,
                "Big5" => Encoding.GetEncoding("big5"),
                _ => Encoding.GetEncoding("gbk")
            };
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private byte[] TextToBytes(string str)
    {
        var encoding = SelectedCoding(SendCodingCombo);
        return encoding.GetBytes(str);
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
        if (ReceiveCodingCombo != null)
            ReceiveCodingCombo.IsEnabled = ReceiveModeCombo.SelectedIndex == 1;
    }

    private void OnSendModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SendCodingCombo != null)
            SendCodingCombo.IsEnabled = SendModeCombo.SelectedIndex == 1;
    }

    // ========== Shell 终端（类 PuTTY） ==========

    /// <summary>
    /// Tab 切换 — 新标签页内容上滑淡入（首次打开不触发）
    /// </summary>
    private bool _tabInitialized;

    private void OnModeTabChanged(object sender, SelectionChangedEventArgs e)
    {
        _activeTabIndex = ModeTabView.SelectedIndex;

        // 首次加载（页面打开）不淡入，只做后续切换时淡入
        if (_tabInitialized && ModeTabView.SelectedItem is TabViewItem tvi &&
            tvi.Content is UIElement content)
        {
            // 上滑 + 淡入组合动画
            var translate = content.RenderTransform as TranslateTransform ?? new TranslateTransform();
            content.RenderTransform = translate;

            var storyboard = new Storyboard();

            var opacityAnim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(opacityAnim, content);
            Storyboard.SetTargetProperty(opacityAnim, "Opacity");
            storyboard.Children.Add(opacityAnim);

            var slideAnim = new DoubleAnimation
            {
                From = 24,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(slideAnim, translate);
            Storyboard.SetTargetProperty(slideAnim, "Y");
            storyboard.Children.Add(slideAnim);

            // 先复位再启动，避免切换时序问题
            content.Opacity = 0;
            translate.Y = 24;
            storyboard.Begin();
        }
        else if (!_tabInitialized)
        {
            _tabInitialized = true;
        }

        if (_activeTabIndex == 1 && ShellInputBox != null)
            ShellInputBox.Focus(FocusState.Programmatic);
    }

    private void OnShellModeChanged(object sender, SelectionChangedEventArgs e)
    {
        _shellPassthrough = ShellModeCombo.SelectedIndex == 1;
        _lastShellInputText = "";
        _shellHistoryIndex = -1;
        // 模式切换时重置仿真器，避免行状态串扰
        _emulator.Clear();
        // Pivot 懒加载：ShellInputBox 可能尚未创建，判空保护
        if (ShellInputBox == null) return;
        _clearingShellInput = true;
        ShellInputBox.Text = "";
        _clearingShellInput = false;
        ShellInputBox.PlaceholderText = _shellPassthrough
            ? "透传模式：输入即发送（仅远端回显）"
            : "仿真终端：输入命令，Enter 发送（↑↓ 历史）";
    }

    private void OnShellInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_shellPassthrough) return;

        if (e.Key == VirtualKey.Enter)
        {
            // 多行模式：Enter 换行，Ctrl+Enter 发送
            var ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            if (_shellExpanded && !ctrl) return; // 不拦截，让文本框插入换行
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
    /// 行模式发送：命令回显到终端 + 发送整行 + 回车（嵌入式 CLI 普遍识别 CR）
    /// </summary>
    private void SendShellLine()
    {
        var line = ShellInputBox.Text;
        if (string.IsNullOrEmpty(line)) return;

        // 回显命令（终端风格提示符）
        AppendShellOutput($"> {line}\r\n");

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

    /// <summary>
    /// 向终端输出区追加文本并自动滚动到底部。
    /// 仿真终端模式：过 TerminalEmulator 处理退格/回车/ANSI。
    ///   纯追加走增量（只追加新增段），改写（回车覆盖/退格/清屏）才全量重设，避免高频流卡顿。
    /// 透传模式：原始文本原样追加。
    /// </summary>
    private void AppendShellOutput(string text)
    {
        if (_shellPassthrough)
        {
            ShellOutputBox.Text += text;
            if (ShellOutputBox.Text.Length > 50000)
                ShellOutputBox.Text = ShellOutputBox.Text[^25000..];
        }
        else
        {
            // 增量：纯追加只拼新增段；退格/回车覆盖/清屏等改写才全量
            if (_emulator.Feed(text))
                ShellOutputBox.Text = _emulator.Text;
            else
                ShellOutputBox.Text += _emulator.TakeAppend();
        }

        // 自动滚动开关（设置 → 串口助手 → Shell 终端自动滚动）
        if (AppSettings.ShellAutoScroll)
        {
            ShellOutputBox.SelectionStart = ShellOutputBox.Text.Length;
            ShellOutputBox.SelectionLength = 0;
        }
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
            var encoding = SelectedCoding(ShellCodingCombo);
            var data = encoding.GetBytes(text);
            _serialPort.Write(data, 0, data.Length);
        }
        catch (Exception ex)
        {
            StatusBar.Text = $"发送失败: {ex.Message}";
        }
    }

    private void OnClearShell(object sender, RoutedEventArgs e)
    {
        _emulator.Clear();
        ShellOutputBox.Text = "";
    }

    // ========== 信号发送（终端中断/EOF 等） ==========

    private void OnShellSendCtrlC(object sender, RoutedEventArgs e) => SendShellBytes("\x03");
    private void OnShellSendCtrlD(object sender, RoutedEventArgs e) => SendShellBytes("\x04");
    private void OnShellSendCtrlZ(object sender, RoutedEventArgs e) => SendShellBytes("\x1a");
    private void OnShellSendEnter(object sender, RoutedEventArgs e) => SendShellBytes("\r");

    /// <summary>展开/收起输入框为多行模式。</summary>
    private void OnShellToggleExpand(object sender, RoutedEventArgs e)
    {
        _shellExpanded = !_shellExpanded;
        if (_shellExpanded)
        {
            // 多行：铺满剩余高度，AcceptsReturn 允许换行，Ctrl+Enter 发送
            ShellInputBox.AcceptsReturn = true;
            ShellInputBox.TextWrapping = TextWrapping.Wrap;
            ShellInputBox.Height = double.NaN;
            ShellInputBox.VerticalContentAlignment = VerticalAlignment.Top;
            ShellInputBox.Padding = new Thickness(26, 8, 8, 8);
            ShellInputBox.MinHeight = 120;
            ShellInputBox.PlaceholderText = "多行输入（Enter 换行，Ctrl+Enter 发送）";
            ShellSendBtn.Visibility = Visibility.Visible;
            ShellExpandGlyph.Glyph = "\uE70E"; // ChevronUp
        }
        else
        {
            // 单行：Enter 发送，↑↓ 历史
            ShellInputBox.AcceptsReturn = false;
            ShellInputBox.TextWrapping = TextWrapping.NoWrap;
            ShellInputBox.Height = 40;
            ShellInputBox.MinHeight = 0;
            ShellInputBox.VerticalContentAlignment = VerticalAlignment.Center;
            ShellInputBox.Padding = new Thickness(26, 12, 8, 12);
            ShellInputBox.PlaceholderText = _shellPassthrough
                ? "透传模式：输入即发送（仅远端回显）"
                : "输入命令，Enter 发送（↑↓ 历史）";
            ShellSendBtn.Visibility = Visibility.Collapsed;
            ShellExpandGlyph.Glyph = "\uE70D"; // ChevronDown
        }
        ShellInputBox.Focus(FocusState.Programmatic);
    }
}
