namespace TOfont.Core.Models;

/// <summary>
/// 扫描模式 — 决定点阵到字节数组的排列方式
/// </summary>
public enum ScanMode
{
    /// <summary>逐行式：每行从左到右，每8点为一字节，换行继续</summary>
    RowMajor,

    /// <summary>逐列式：每列从上到下，每8点为一字节，换列继续</summary>
    ColumnMajor,

    /// <summary>逐行进位式：每行从左到右，最高位在上</summary>
    RowProgressive,

    /// <summary>逐列进位式：每列从上到下，最高位在左</summary>
    ColumnProgressive,
}
