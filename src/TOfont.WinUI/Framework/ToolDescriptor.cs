namespace TOfont.WinUI.Framework;

/// <summary>
/// 工具描述 — 一个工具在工具箱中的元数据。
/// 新工具只需在 <see cref="ToolCatalog"/> 中登记一条，导航与首页卡片自动生成。
/// </summary>
public class ToolDescriptor
{
    /// <summary>工具唯一标识（用于导航 Tag）</summary>
    public string Id { get; init; } = "";

    /// <summary>显示名称</summary>
    public string Title { get; init; } = "";

    /// <summary>Segoe MDL2 Assets 图标码</summary>
    public string Glyph { get; init; } = "";

    /// <summary>工具页面类型（继承 Page）</summary>
    public Type PageType { get; init; } = null!;

    /// <summary>是否显示在首页卡片</summary>
    public bool ShowInHome { get; init; } = true;
}
