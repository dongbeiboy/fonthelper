namespace TOfont.WinUI;

public static class AppSettings
{
    public static int ScanMode { get; set; }
    public static bool MsbFirst { get; set; } = true;
    public static bool LitIs1 { get; set; } = true;
    public static bool UseHex { get; set; } = true;

    // 串口助手：Shell 终端收到数据自动滚动到底部
    public static bool ShellAutoScroll { get; set; } = true;

    // CLI 模式：通过本地 HTTP 端口暴露串口助手能力给 agent 工具
    public static bool CliEnabled { get; set; }
    public static int CliPort { get; set; } = 8765;
}
