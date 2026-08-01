using TOfont.WinUI.Tools.FontExtraction;
using TOfont.WinUI.Tools.SerialPortTool;

namespace TOfont.WinUI.Framework;

/// <summary>
/// 工具注册表 — 工具箱中所有工具的单一登记处。
/// 新增工具：在此列表追加一行，导航与首页自动更新。
/// </summary>
public static class ToolCatalog
{
    public static IReadOnlyList<ToolDescriptor> Tools { get; } =
    [
        new()
        {
            Id = "font-extraction",
            Title = "字模提取",
            Glyph = "\uE943",
            PageType = typeof(FontExtractionPage)
        },
        new()
        {
            Id = "serial-port",
            Title = "串口助手",
            Glyph = "\uE950",
            PageType = typeof(SerialPortPage)
        },
        // 未来新工具在此追加一行即可
    ];

    public static ToolDescriptor? FindById(string id) =>
        Tools.FirstOrDefault(t => t.Id == id);
}
