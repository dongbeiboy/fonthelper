namespace TOfont.Core.Models;

/// <summary>
/// 点阵数据 — 承载取模后的二维像素矩阵
/// </summary>
public class DotMatrix
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }

    public DotMatrix(int width, int height)
    {
        Width = width;
        Height = height;
        Data = new byte[(width * height + 7) / 8];
    }

    public DotMatrix(int width, int height, byte[] data)
    {
        Width = width;
        Height = height;
        Data = data;
    }

    public bool GetPixel(int x, int y)
    {
        var index = y * Width + x;
        return (Data[index / 8] & (1 << (7 - index % 8))) != 0;
    }

    public void SetPixel(int x, int y, bool value)
    {
        var index = y * Width + x;
        if (value)
            Data[index / 8] |= (byte)(1 << (7 - index % 8));
        else
            Data[index / 8] &= (byte)~(1 << (7 - index % 8));
    }
}
