using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using TOfont.Core.Extraction;
using TOfont.Core.Formatting;
using TOfont.Core.Models;
using TOfont.Core.Parsing;

namespace TOfont.WinUI.Pages;

public sealed partial class ExtractionPage : Page
{
    private bool _isTextMode = true;
    private bool _isFontLibMode;
    private bool _isImportMode;
    private byte[]? _currentDotData;
    private int _currentWidth;
    private int _currentHeight;
    private int _zoom = 8;
    private List<GlyphInfo>? _lastGlyphs;
    private DotMatrix? _lastMatrix;
    private string[] _ptlChars = [];
    private int? _presetCols;
    private int? _presetRows;

    public ExtractionPage()
    {
        InitializeComponent();
        ModeSwitch.ItemsSource = new[] { "文字取模", "图片取模", "字体库", "字库导入" };
        Loaded += (_, _) =>
        {
            PopulateFonts();
            RenderEmptyGrid();
        };
        ActualThemeChanged += (_, _) => ReRender();
    }

    private void PopulateFonts()
    {
        foreach (var family in System.Drawing.FontFamily.Families)
            FontCombo.Items.Add(family.Name);

        FontCombo.SelectedIndex = FontCombo.Items.IndexOf("Microsoft YaHei");
        if (FontCombo.SelectedIndex < 0) FontCombo.SelectedIndex = 0;
    }

    private (int cols, int rows) GetViewportGridSize()
    {
        var vpw = PreviewScroll.ViewportWidth;
        var vph = PreviewScroll.ViewportHeight;
        if (vpw > 10 && vph > 10)
            return ((int)(vpw / _zoom) + 1, (int)(vph / _zoom) + 1);
        return (40, 30);
    }

    private void OnSizeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (SizeWarning != null)
            SizeWarning.Visibility = args.NewValue <= 6 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCanvasPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        var preset = (CanvasPreset.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "自由";
        if (preset == "自由")
        {
            _presetCols = null;
            _presetRows = null;
        }
        else
        {
            var parts = preset.Split('×');
            _presetCols = int.Parse(parts[0]);
            _presetRows = int.Parse(parts[1]);
        }
        if (PreviewImage != null)
        {
            PreviewImage.Opacity = 0;
            ReRender();
            var anim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(anim, PreviewImage);
            Storyboard.SetTargetProperty(anim, "Opacity");
            var sb = new Storyboard();
            sb.Children.Add(anim);
            sb.Begin();
        }
    }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        var idx = ModeSwitch.SelectedIndex;
        _isTextMode = idx == 0;
        _isFontLibMode = idx == 2;
        _isImportMode = idx == 3;

        if (TextPanel == null) return;

        TextPanel.Visibility = _isTextMode ? Visibility.Visible : Visibility.Collapsed;
        ImagePanel.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        FontLibPanel.Visibility = _isFontLibMode ? Visibility.Visible : Visibility.Collapsed;
        FontImportPanel.Visibility = _isImportMode ? Visibility.Visible : Visibility.Collapsed;
        SaveBtn.Visibility = (_isFontLibMode || _isImportMode) ? Visibility.Visible : Visibility.Collapsed;

        _lastGlyphs = null;
        _lastMatrix = null;
        RenderEmptyGrid();
    }

    private void OnGoSettings(object sender, RoutedEventArgs e)
    {
        MainWindow.NavigateTo("settings");
    }

    private void OnExtract(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_isFontLibMode)
                ExtractFontLib();
            else if (_isImportMode)
                ImportFont();
            else if (_isTextMode)
                ExtractText();
            else
                ExtractImage();

            StatusBar.Text = $"宽{_currentWidth}×高{_currentHeight} | {_currentDotData?.Length ?? 0} 字节 | {AppSettings.ScanMode switch { 0 => "逐行", 1 => "逐列", 2 => "逐行进位", _ => "逐列进位" }}";
        }
        catch (Exception ex)
        {
            OutputBox.Text = $"错误: {ex.Message}";
            StatusBar.Text = "取模失败";
        }
    }

    private void ExtractText()
    {
        var text = InputTextBox.Text;
        if (string.IsNullOrEmpty(text)) return;

        var extractor = new FontExtractor
        {
            FontSize = (int)SizeBox.Value,
            Bold = BoldChk.IsChecked == true,
            Italic = ItalicChk.IsChecked == true
        };

        var glyphs = extractor.ExtractRange(text, FontCombo.SelectedItem?.ToString() ?? "Microsoft YaHei");
        var allData = new List<byte>();
        _currentWidth = 0;
        _currentHeight = 0;

        foreach (var glyph in glyphs)
        {
            var converted = DotMatrixConverter.Convert(glyph.DotData, glyph.Width, glyph.Height,
                (ScanMode)AppSettings.ScanMode, AppSettings.MsbFirst, AppSettings.LitIs1);

            allData.AddRange(converted);
            if (_currentWidth == 0)
            {
                _currentWidth = glyph.Width;
                _currentHeight = glyph.Height;
            }
        }

        _currentDotData = allData.ToArray();

        var fmt = new OutputFormat
        {
            UseHex = AppSettings.UseHex,
            Prefix = "unsigned char",
            Comment = "//"
        };
        var formatter = new OutputFormatter(fmt);

        var sb = new System.Text.StringBuilder();
        foreach (var glyph in glyphs)
        {
            var converted = DotMatrixConverter.Convert(glyph.DotData, glyph.Width, glyph.Height,
                (ScanMode)AppSettings.ScanMode, AppSettings.MsbFirst, AppSettings.LitIs1);
            var g = new GlyphInfo
            {
                Character = glyph.Character,
                Width = glyph.Width,
                Height = glyph.Height,
                DotData = converted
            };
            sb.Append(formatter.FormatGlyph(g));
        }
        OutputBox.Text = sb.ToString();

        if (glyphs.Count > 0)
        {
            _lastGlyphs = glyphs;
            _lastMatrix = null;
            RenderPreviewAll(glyphs);
        }
    }

    private void ExtractFontLib()
    {
        if (_ptlChars.Length == 0)
        {
            OutputBox.Text = "请先加载 PTL 字符列表文件";
            return;
        }

        var fontSize = (int)SizeBox.Value;
        var extractor = new FontExtractor
        {
            FontSize = fontSize,
            Bold = BoldChk.IsChecked == true,
            Italic = ItalicChk.IsChecked == true
        };

        var glyphs = extractor.ExtractRange(new string(_ptlChars.SelectMany(c => c.ToCharArray()).ToArray()),
            FontCombo.SelectedItem?.ToString() ?? "Microsoft YaHei");

        var allData = new List<byte>();
        _currentWidth = glyphs.FirstOrDefault()?.Width ?? fontSize;
        _currentHeight = glyphs.FirstOrDefault()?.Height ?? fontSize;

        var hex = AppSettings.UseHex;
        var sb = new System.Text.StringBuilder();

        var totalBytes = 0;
        var charInfo = new List<(string ch, int offset, int w, int h)>();
        foreach (var glyph in glyphs)
        {
            var converted = DotMatrixConverter.Convert(glyph.DotData, glyph.Width, glyph.Height,
                (ScanMode)AppSettings.ScanMode, AppSettings.MsbFirst, AppSettings.LitIs1);
            charInfo.Add((glyph.Character.ToString(), totalBytes, glyph.Width, glyph.Height));
            allData.AddRange(converted);
            totalBytes += converted.Length;
        }

        var varName = "font_" + (FontCombo.SelectedItem?.ToString()?.Replace(" ", "_") ?? "data");
        sb.AppendLine($"// 字体库: {FontCombo.SelectedItem?.ToString()} @ {fontSize}px");
        sb.AppendLine($"// 字符数: {glyphs.Count}, 总字节: {totalBytes}");
        sb.AppendLine();

        sb.AppendLine("// 索引表: {偏移, 宽, 高}");
        sb.AppendLine($"static const uint16_t {varName}_index[{glyphs.Count}][3] = {{");
        for (var i = 0; i < charInfo.Count; i++)
        {
            var (ch, offset, w, h) = charInfo[i];
            var hexChar = hex ? $"0x{(int)ch[0]:X4}" : $"{(int)ch[0]}";
            sb.AppendLine($"    {{{offset}, {w}, {h}}}, // [{i}] '{ch}' = U+{hexChar}");
        }
        sb.AppendLine("};");
        sb.AppendLine();

        sb.AppendLine($"static const unsigned char {varName}_data[] = {{");
        for (var i = 0; i < allData.Count; i++)
        {
            if (i % 16 == 0) sb.Append("    ");
            sb.Append(hex ? $"0x{allData[i]:X2}" : $"{allData[i]}");
            if (i < allData.Count - 1) sb.Append(", ");
            if (i % 16 == 15) sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("};");

        OutputBox.Text = sb.ToString();
        StatusBar.Text = $"字体库: {glyphs.Count} 字符 | {totalBytes} 字节";

        var preview = glyphs.Take(Math.Min(glyphs.Count, 8)).ToList();
        _lastGlyphs = preview;
        _lastMatrix = null;
        if (preview.Count > 0)
            RenderPreviewAll(preview);
    }

    private void ExtractImage()
    {
        var path = ImagePathBox.Text;
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            OutputBox.Text = "请选择有效图片文件";
            return;
        }

        var extractor = new ImageExtractor
        {
            Threshold = (int)ThresholdBox.Value,
            CropX = (int)CropXBox.Value,
            CropY = (int)CropYBox.Value
        };

        if (double.IsNaN(CropWBox.Value))
            extractor.CropWidth = null;
        else
            extractor.CropWidth = (int)CropWBox.Value;

        if (double.IsNaN(CropHBox.Value))
            extractor.CropHeight = null;
        else
            extractor.CropHeight = (int)CropHBox.Value;

        if (!double.IsNaN(ScaleWBox.Value))
            extractor.TargetWidth = (int)ScaleWBox.Value;
        if (!double.IsNaN(ScaleHBox.Value))
            extractor.TargetHeight = (int)ScaleHBox.Value;

        var matrix = extractor.Extract(path);
        var converted = DotMatrixConverter.Convert(matrix.Data, matrix.Width, matrix.Height,
                (ScanMode)AppSettings.ScanMode, AppSettings.MsbFirst, AppSettings.LitIs1);

        _currentDotData = converted;
        _currentWidth = matrix.Width;
        _currentHeight = matrix.Height;

        var fmt = new OutputFormat { UseHex = AppSettings.UseHex };
        var formatter = new OutputFormatter(fmt);
        var glyph = new GlyphInfo
        {
            Character = ' ',
            Width = matrix.Width,
            Height = matrix.Height,
            DotData = converted
        };
        OutputBox.Text = formatter.FormatGlyph(glyph, "image");

        _lastMatrix = matrix;
        _lastGlyphs = null;
        RenderBitmap(matrix);
    }

    private async void OnBrowseFontFile(object sender, RoutedEventArgs e)
    {
        var hwnd = MainWindow.GetHandle();
        if (hwnd == IntPtr.Zero) return;

        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file != null)
            FontFileBox.Text = file.Path;
    }

    private void ImportFont()
    {
        var path = FontFileBox.Text;
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            OutputBox.Text = "请选择有效字库文件";
            return;
        }

        var w = (int)FontWidthBox.Value;
        var h = (int)FontHeightBox.Value;
        var offset = (int)FileOffsetBox.Value;

        var glyphs = FontImporter.Import(path, w, h, offset);

        var allData = new List<byte>();
        foreach (var g in glyphs)
            allData.AddRange(g.DotData);

        _currentDotData = allData.ToArray();
        _currentWidth = w;
        _currentHeight = h;

        var fmt = new OutputFormat { UseHex = AppSettings.UseHex };
        var formatter = new OutputFormatter(fmt);
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"// 导入字库: {path}");
        sb.AppendLine($"// 字符数: {glyphs.Count}, 每字: {w}×{h}, 起始偏移: {offset}");
        sb.AppendLine();

        for (var i = 0; i < glyphs.Count; i++)
        {
            var g = glyphs[i];
            sb.Append(formatter.FormatGlyph(g, $"char_{i:X4}"));
        }

        OutputBox.Text = sb.ToString();
        StatusBar.Text = $"字库导入: {glyphs.Count} 字符 | {allData.Count} 字节";

        var preview = glyphs.Take(Math.Min(glyphs.Count, 16)).ToList();
        _lastGlyphs = preview;
        _lastMatrix = null;
        if (preview.Count > 0)
            RenderPreviewAll(preview);
    }

    // ========== Bitmap-based rendering ==========

    private static void SetPixel(byte[] pixels, int stride, int x, int y, byte b, byte g, byte r)
    {
        var off = y * stride + x * 4;
        pixels[off] = b;
        pixels[off + 1] = g;
        pixels[off + 2] = r;
        pixels[off + 3] = 255;
    }

    private void SetZoomedCell(byte[] pixels, int stride, int gx, int gy, byte b, byte g, byte r)
    {
        var startX = gx * _zoom;
        var startY = gy * _zoom;
        for (var py = 0; py < _zoom; py++)
        for (var px = 0; px < _zoom; px++)
            SetPixel(pixels, stride, startX + px, startY + py, b, g, r);
    }

    private void SetZoomedCellBorder(byte[] pixels, int stride, int gx, int gy, byte b, byte g, byte r)
    {
        var startX = gx * _zoom;
        var startY = gy * _zoom;
        for (var px = 0; px < _zoom; px++)
        {
            SetPixel(pixels, stride, startX + px, startY, b, g, r);
            SetPixel(pixels, stride, startX + px, startY + _zoom - 1, b, g, r);
        }
        for (var py = 0; py < _zoom; py++)
        {
            SetPixel(pixels, stride, startX, startY + py, b, g, r);
            SetPixel(pixels, stride, startX + _zoom - 1, startY + py, b, g, r);
        }
    }

    private void FlushBitmap(int w, int h, byte[] pixels)
    {
        var wb = new WriteableBitmap(w, h);
        using var stream = wb.PixelBuffer.AsStream();
        stream.Write(pixels, 0, pixels.Length);
        wb.Invalidate();
        PreviewImage.Source = wb;
        PreviewImage.Width = w;
        PreviewImage.Height = h;
    }

    private (byte b, byte g, byte r) GetDotColor()
    {
        var v = ActualTheme == ElementTheme.Dark ? (byte)255 : (byte)0;
        return (v, v, v);
    }

    private (byte b, byte g, byte r) GetBgColor()
    {
        var v = ActualTheme == ElementTheme.Dark ? (byte)40 : (byte)255;
        return (v, v, v);
    }

    private void RenderPreviewAll(List<GlyphInfo> glyphs)
    {
        var maxFW = _presetCols ?? 64;
        var maxFH = _presetRows ?? 64;
        var frameW = Math.Min(glyphs.Max(g => g.Width), maxFW);
        var frameH = Math.Min(glyphs.Max(g => g.Height), maxFH);
        var (vpCols, vpRows) = GetViewportGridSize();

        var charOffsets = new int[glyphs.Count];
        var gap = 2;
        var cum = 0;
        for (var i = 0; i < glyphs.Count; i++)
        {
            charOffsets[i] = cum;
            cum += frameW + gap;
        }
        var totalCols = _presetCols ?? Math.Max(cum - gap, vpCols);
        var totalRows = _presetRows ?? Math.Max(frameH, vpRows);

        var dms = glyphs.Select(g => new DotMatrix(g.Width, g.Height, g.DotData)).ToArray();

        var bmpW = totalCols * _zoom;
        var bmpH = totalRows * _zoom;
        var stride = bmpW * 4;
        var pixels = new byte[bmpH * stride];

        var (bb, bg, br) = GetBgColor();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = bb;
            pixels[i + 1] = bg;
            pixels[i + 2] = br;
            pixels[i + 3] = 255;
        }

        var (db, dg, dr) = GetDotColor();
        for (var gy = 0; gy < totalRows; gy++)
        for (var gx = 0; gx < totalCols; gx++)
        {
            var charIdx = -1;
            var localX = 0;
            for (var i = 0; i < glyphs.Count; i++)
            {
                if (gx >= charOffsets[i] && gx < charOffsets[i] + frameW)
                {
                    charIdx = i;
                    localX = gx - charOffsets[i];
                    break;
                }
            }

            var dot = false;
            if (charIdx >= 0)
            {
                var g = glyphs[charIdx];
                if (localX < g.Width && gy < g.Height)
                    dot = dms[charIdx].GetPixel(localX, gy);
            }

            if (dot)
                SetZoomedCell(pixels, stride, gx, gy, db, dg, dr);
            SetZoomedCellBorder(pixels, stride, gx, gy, 140, 140, 140);
        }

        FlushBitmap(bmpW, bmpH, pixels);
        PreviewScroll?.ChangeView(0, 0, null);
    }

    private void RenderEmptyGrid()
    {
        var cols = _presetCols ?? 80;
        var rows = _presetRows ?? 60;
        RenderEmptyGrid(cols, rows);
    }

    private void RenderEmptyGrid(int cols, int rows)
    {
        var bmpW = cols * _zoom;
        var bmpH = rows * _zoom;
        var stride = bmpW * 4;
        var pixels = new byte[bmpH * stride];

        var (bb, bg, br) = GetBgColor();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = bb;
            pixels[i + 1] = bg;
            pixels[i + 2] = br;
            pixels[i + 3] = 255;
        }

        for (var gy = 0; gy < rows; gy++)
        for (var gx = 0; gx < cols; gx++)
            SetZoomedCellBorder(pixels, stride, gx, gy, 140, 140, 140);

        FlushBitmap(bmpW, bmpH, pixels);
        PreviewScroll?.ChangeView(0, 0, null);
    }

    private void RenderBitmap(DotMatrix matrix)
    {
        var cols = _presetCols ?? matrix.Width;
        var rows = _presetRows ?? matrix.Height;
        var bmpW = cols * _zoom;
        var bmpH = rows * _zoom;
        var stride = bmpW * 4;
        var pixels = new byte[bmpH * stride];

        var (bb, bg, br) = GetBgColor();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = bb;
            pixels[i + 1] = bg;
            pixels[i + 2] = br;
            pixels[i + 3] = 255;
        }

        var (db, dg, dr) = GetDotColor();
        for (var gy = 0; gy < rows; gy++)
        for (var gx = 0; gx < cols; gx++)
        {
            if (gx < matrix.Width && gy < matrix.Height && matrix.GetPixel(gx, gy))
                SetZoomedCell(pixels, stride, gx, gy, db, dg, dr);
            SetZoomedCellBorder(pixels, stride, gx, gy, 140, 140, 140);
        }

        FlushBitmap(bmpW, bmpH, pixels);
        PreviewScroll?.ChangeView(0, 0, null);
    }

    private void OnZoomIn(object sender, RoutedEventArgs e)
    {
        _zoom = Math.Min(_zoom * 2, 64);
        ZoomLabel.Text = $"{_zoom}px";
        ReRender();
    }

    private void OnZoomOut(object sender, RoutedEventArgs e)
    {
        _zoom = Math.Max(_zoom / 2, 4);
        ZoomLabel.Text = $"{_zoom}px";
        ReRender();
    }

    private void ReRender()
    {
        if (_lastGlyphs != null)
            RenderPreviewAll(_lastGlyphs);
        else if (_lastMatrix != null)
            RenderBitmap(_lastMatrix);
        else
            RenderEmptyGrid();
    }

    private void OnCopyOutput(object sender, RoutedEventArgs e)
    {
        Windows.ApplicationModel.DataTransfer.DataPackage pkg = new();
        pkg.SetText(OutputBox.Text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
    }

    private async void OnBrowseImage(object sender, RoutedEventArgs e)
    {
        var hwnd = MainWindow.GetHandle();
        if (hwnd == IntPtr.Zero) return;

        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");

        var file = await picker.PickSingleFileAsync();
        if (file != null)
            ImagePathBox.Text = file.Path;
    }

    private async void OnBrowsePtl(object sender, RoutedEventArgs e)
    {
        var hwnd = MainWindow.GetHandle();
        if (hwnd == IntPtr.Zero) return;

        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.FileTypeFilter.Add(".ptl");

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            PtlPathBox.Text = file.Path;
            LoadPtl(file.Path);
        }
    }

    private void LoadPtl(string path)
    {
        try
        {
            var reader = new PtlReader();
            _ptlChars = reader.Read(path);
            PtlCharCount.Text = $"已加载 {_ptlChars.Length} 个字符";
            if (_ptlChars.Length > 0)
                PtlCharCount.Text += $" (例: {string.Join(", ", _ptlChars.Take(10).Select(c => string.IsNullOrWhiteSpace(c) ? $"0x{(int)c[0]:X2}" : c))}...)";
        }
        catch (Exception ex)
        {
            PtlCharCount.Text = $"加载失败: {ex.Message}";
        }
    }

    private async Task PickOutputFileAsync()
    {
        var hwnd = MainWindow.GetHandle();
        if (hwnd == IntPtr.Zero) return;

        var picker = new Windows.Storage.Pickers.FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.FileTypeChoices.Add("C Header", [".h"]);
        picker.FileTypeChoices.Add("C Source", [".c"]);
        picker.SuggestedFileName = "font_data.h";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
            OutputPathBox.Text = file.Path;
    }

    private async void OnBrowseOutput(object sender, RoutedEventArgs e)
    {
        await PickOutputFileAsync();
    }

    private async void OnSaveOutput(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(OutputBox.Text)) return;

        if (string.IsNullOrEmpty(OutputPathBox.Text))
        {
            await PickOutputFileAsync();
            if (string.IsNullOrEmpty(OutputPathBox.Text)) return;
        }

        try
        {
            System.IO.File.WriteAllText(OutputPathBox.Text, OutputBox.Text);
            StatusBar.Text = $"已保存: {OutputPathBox.Text}";
        }
        catch (Exception ex)
        {
            OutputBox.Text = $"保存失败: {ex.Message}";
        }
    }
}
