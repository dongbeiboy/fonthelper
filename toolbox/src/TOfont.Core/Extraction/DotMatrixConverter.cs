using TOfont.Core.Models;

namespace TOfont.Core.Extraction;

/// <summary>
/// 点阵数据转换器 — 将原始点阵按指定扫描模式重排为字节数组。
/// 对标 PCtoLCD2018 的取模方式配置。
/// </summary>
public static class DotMatrixConverter
{
    /// <summary>
    /// 按扫描模式转换点阵数据
    /// </summary>
    /// <param name="src">源点阵字节数组（逐行排列，高位在前）</param>
    /// <param name="width">图像宽度（像素）</param>
    /// <param name="height">图像高度（像素）</param>
    /// <param name="mode">扫描模式</param>
    /// <param name="msbFirst">true=高位在前，false=低位在前</param>
    /// <param name="litIs1">true=阳码（点亮为1），false=阴码（点亮为0）</param>
    /// <returns>转换后的字节数组</returns>
    public static byte[] Convert(byte[] src, int width, int height, ScanMode mode, bool msbFirst, bool litIs1)
    {
        // 进位模式（RowProgressive/ColumnProgressive）按固定字节块扫描：
        // 每行/每列按 8 位对齐，尾部不足 8 位补 0，字节流长度固定为 块数×8。
        // 若按 width*height 紧凑计算总位数，非 8 倍数尺寸会丢失尾部落差。
        int totalBits;
        if (mode == ScanMode.RowProgressive)
        {
            var bytesPerRow = (width + 7) / 8;
            totalBits = bytesPerRow * 8 * height;
        }
        else if (mode == ScanMode.ColumnProgressive)
        {
            var bytesPerCol = (height + 7) / 8;
            totalBits = bytesPerCol * 8 * width;
        }
        else
        {
            totalBits = width * height;
        }

        var totalBytes = (totalBits + 7) / 8;
        var result = new byte[totalBytes];

        int bitIndex = 0;

        for (int baseStep = 0; baseStep < totalBits; baseStep++)
        {
            // 根据模式计算源像素坐标
            int srcX, srcY;

            if (mode == ScanMode.RowMajor)
            {
                srcX = baseStep % width;
                srcY = baseStep / width;
            }
            else if (mode == ScanMode.ColumnMajor)
            {
                srcY = baseStep % height;
                srcX = baseStep / height;
            }
            else if (mode == ScanMode.RowProgressive)
            {
                // 逐行进位: 排列顺序 = 所有行的第1块(8点) → 所有行的第2块 → ...
                // colBlock 按 height*8 分组: 每组覆盖所有 height 行的同一 8 位块
                var colBlock = baseStep / (height * 8);
                var row = (baseStep / 8) % height;
                var colInBlock = baseStep % 8;
                srcX = colBlock * 8 + colInBlock;
                srcY = row;
            }
            else // ColumnProgressive
            {
                // 逐列进位: 排列顺序 = 所有列的第1块(8点) → 所有列的第2块 → ...
                // rowBlock 按 width*8 分组: 每组覆盖所有 width 列的同一 8 位块
                var rowBlock = baseStep / (width * 8);
                var col = (baseStep / 8) % width;
                var rowInBlock = baseStep % 8;
                srcX = col;
                srcY = rowBlock * 8 + rowInBlock;
            }

            // 块对齐后超出图像边界的位按 0 补齐（不参与阴码反转，保证字节流长度固定）
            if (srcX >= width || srcY >= height)
            {
                bitIndex++;
                continue;
            }

            // 从源数组获取该像素的位值
            var srcBitIndex = srcY * width + srcX;
            var srcByteIndex = srcBitIndex / 8;
            var srcBitOffset = 7 - (srcBitIndex % 8);
            var pixelOn = srcByteIndex < src.Length && (src[srcByteIndex] & (1 << srcBitOffset)) != 0;

            // 阴码反转
            if (!litIs1)
                pixelOn = !pixelOn;

            if (!pixelOn)
            {
                bitIndex++;
                continue;
            }

            // 写入结果
            var dstByteIndex = bitIndex / 8;
            var dstBitOffset = msbFirst ? (7 - (bitIndex % 8)) : (bitIndex % 8);

            if (dstByteIndex < result.Length)
                result[dstByteIndex] |= (byte)(1 << dstBitOffset);

            bitIndex++;
        }

        return result;
    }
}
