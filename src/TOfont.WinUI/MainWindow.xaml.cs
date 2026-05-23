using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using TOfont.WinUI.Pages;

namespace TOfont.WinUI;

public sealed partial class MainWindow : Window
{
    private static MainWindow? _current;

    private bool _isActive = true;
    private bool _isDark;

    private HomePage? _homePage;
    private ExtractionPage? _extractionPage;
    private SettingsPage? _settingsPage;

    public MainWindow()
    {
        _current = this;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        try { SystemBackdrop = new MicaBackdrop(); } catch { }
        ContentFrame.Navigate(typeof(HomePage));
        _homePage = ContentFrame.Content as HomePage;
        NavView.SelectedItem = NavView.MenuItems[0];

        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionString = ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "";
        TitleText.Text = $"TOfont {versionString}";

        if (Content is FrameworkElement root)
            root.ActualThemeChanged += (_, _) => { RefreshTheme(); UpdateTitleBar(); };
        TitleBarArea.Loaded += (_, _) =>
        {
            RefreshTheme();
            UpdateTitleBar();
            SetTitleBarDragRegion();
        };
        TitleBarArea.SizeChanged += (_, _) => SetTitleBarDragRegion();
        Activated += (_, args) =>
        {
            _isActive = args.WindowActivationState != Microsoft.UI.Xaml.WindowActivationState.Deactivated;
            UpdateTitleBar();
            SetWindowIcon();
        };
    }

    public static IntPtr GetHandle()
    {
        if (_current == null) return IntPtr.Zero;
        return WinRT.Interop.WindowNative.GetWindowHandle(_current);
    }

    public static void NavigateTo(string tag)
    {
        if (_current == null) return;
        foreach (var item in _current.NavView.MenuItems)
        {
            if (item is NavigationViewItem nvi && nvi.Tag is string t && t == tag)
            {
                _current.NavView.SelectedItem = nvi;
                return;
            }
        }
        foreach (var item in _current.NavView.FooterMenuItems)
        {
            if (item is NavigationViewItem nvi && nvi.Tag is string t && t == tag)
            {
                _current.NavView.SelectedItem = nvi;
                return;
            }
        }
    }

    private bool IsDark => _isDark;

    private void RefreshTheme()
    {
        try { _isDark = (Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark; }
        catch { }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            Page? target = null;
            switch (tag)
            {
                case "home":
                    _homePage ??= new HomePage();
                    target = _homePage;
                    break;
                case "extraction":
                    _extractionPage ??= new ExtractionPage();
                    target = _extractionPage;
                    break;
                case "settings":
                    _settingsPage ??= new SettingsPage();
                    target = _settingsPage;
                    break;
            }
            if (target != null && !ReferenceEquals(ContentFrame.Content, target))
                ContentFrame.Content = target;
        }
    }

    private void SetTitleBarDragRegion()
    {
        if (AppWindow == null) return;
        var scale = TitleBarArea.XamlRoot.RasterizationScale;
        var w = (int)(TitleBarArea.ActualWidth * scale);
        var h = (int)(TitleBarArea.ActualHeight * scale);
        var rects = new[] { new Windows.Graphics.RectInt32(0, 0, w, h) };
        AppWindow.TitleBar.SetDragRectangles(rects);
    }

    private void SetWindowIcon()
    {
        try
        {
            var hwnd = GetHandle();
            if (hwnd == IntPtr.Zero) return;
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png");
            if (!System.IO.File.Exists(iconPath)) return;
            using var bitmap = new System.Drawing.Bitmap(iconPath);
            var iconHandle = bitmap.GetHicon();
            const uint WM_SETICON = 0x0080;
            SendMessage(hwnd, WM_SETICON, 0, iconHandle);
            SendMessage(hwnd, WM_SETICON, 1, iconHandle);
        }
        catch { }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, int wParam, IntPtr lParam);

    private void UpdateTitleBar()
    {
        if (AppWindow == null) return;
        var isDark = IsDark;
        var fg = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        var hoverBg = isDark
            ? Windows.UI.Color.FromArgb(255, 60, 60, 60)
            : Windows.UI.Color.FromArgb(255, 220, 220, 220);
        var transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        AppWindow.TitleBar.BackgroundColor = transparent;
        AppWindow.TitleBar.InactiveBackgroundColor = transparent;
        AppWindow.TitleBar.ButtonBackgroundColor = transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = transparent;
        AppWindow.TitleBar.ButtonForegroundColor = fg;
        AppWindow.TitleBar.ButtonHoverForegroundColor = fg;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = hoverBg;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = isDark ? Microsoft.UI.Colors.Gray : Microsoft.UI.Colors.DimGray;
        TitleBarArea.Background = null;
    }
}
