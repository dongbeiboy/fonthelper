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
        // 总位数和总字节数
        var totalBits = width * height;
        var totalBytes = (totalBits + 7) / 8;
        var result = new byte[totalBytes];

        int bitIndex = 0;

        var (dx, dy) = mode switch
        {
            ScanMode.RowMajor     => ( 1,  0), // 逐行：左→右
            ScanMode.ColumnMajor  => ( 0,  1), // 逐列：上→下
            ScanMode.RowProgressive => ( 1,  8), // 逐行进位
            ScanMode.ColumnProgressive => ( 8,  1), // 逐列进位
            _                     => ( 1,  0)
        };

        for (int baseStep = 0; baseStep < totalBits; baseStep++)
        {
            // 根据模式计算源像素坐标
            int srcX, srcY;

            if (mode is ScanMode.RowMajor or ScanMode.ColumnMajor)
            {
                if (mode == ScanMode.RowMajor)
                {
                    srcX = baseStep % width;
                    srcY = baseStep / width;
                }
                else // ColumnMajor
                {
                    srcY = baseStep % height;
                    srcX = baseStep / height;
                }
            }
            else if (mode == ScanMode.RowProgressive)
            {
                // 逐行进位: 取第1行前8点→第1字节, 第2行前8点→第2字节...
                // 即字节0= row0.col[0..7], 字节1= row1.col[0..7], ..., 字节8= row0.col[8..15]...
                var colBlock = baseStep / (height * 8);
                var row = (baseStep / 8) % height;
                var colInBlock = baseStep % 8;
                srcX = colBlock * 8 + colInBlock;
                srcY = row;
            }
            else // ColumnProgressive
            {
                // 逐列进位: 取第1列前8点→第1字节, 第2列前8点→第2字节...
                var rowBlock = baseStep / (width * 8);
                var col = (baseStep / 8) % width;
                var rowInBlock = baseStep % 8;
                srcX = col;
                srcY = rowBlock * 8 + rowInBlock;
            }

            if (srcX >= width || srcY >= height)
                continue;

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
