using System.ComponentModel;
using System.Runtime.CompilerServices;
using TOfont.Core.Models;

namespace TOfont.WinUI.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private string _inputText = "你好";
    private string _outputCode = "";
    private string _fontFamily = "Microsoft YaHei";
    private int _fontSize = 16;
    private bool _bold;
    private bool _italic;
    private ScanMode _scanMode = ScanMode.RowMajor;
    private bool _msbFirst = true;
    private bool _litIs1 = true;
    private bool _useHex = true;
    private int _threshold = 128;
    private bool _isTextMode = true;
    private bool[,] _previewPixels = new bool[0, 0];
    private int _zoom = 16;
    private string _imagePath = "";

    public string InputText { get => _inputText; set { _inputText = value; OnPropertyChanged(); } }
    public string OutputCode { get => _outputCode; set { _outputCode = value; OnPropertyChanged(); } }
    public string FontFamily { get => _fontFamily; set { _fontFamily = value; OnPropertyChanged(); } }
    public int FontSize { get => _fontSize; set { _fontSize = value; OnPropertyChanged(); } }
    public bool Bold { get => _bold; set { _bold = value; OnPropertyChanged(); } }
    public bool Italic { get => _italic; set { _italic = value; OnPropertyChanged(); } }
    public ScanMode ScanMode { get => _scanMode; set { _scanMode = value; OnPropertyChanged(); } }
    public bool MsbFirst { get => _msbFirst; set { _msbFirst = value; OnPropertyChanged(); } }
    public bool LitIs1 { get => _litIs1; set { _litIs1 = value; OnPropertyChanged(); } }
    public bool UseHex { get => _useHex; set { _useHex = value; OnPropertyChanged(); } }
    public int Threshold { get => _threshold; set { _threshold = value; OnPropertyChanged(); } }
    public bool IsTextMode { get => _isTextMode; set { _isTextMode = value; OnPropertyChanged(); } }
    public int Zoom { get => _zoom; set { _zoom = value; OnPropertyChanged(); } }
    public string ImagePath { get => _imagePath; set { _imagePath = value; OnPropertyChanged(); } }
    public bool[,] PreviewPixels { get => _previewPixels; set { _previewPixels = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
