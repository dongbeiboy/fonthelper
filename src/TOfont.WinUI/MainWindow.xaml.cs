using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TOfont.WinUI.Framework;
using TOfont.WinUI.Pages;
using Windows.System;

namespace TOfont.WinUI;

public sealed partial class MainWindow : Window
{
    private static MainWindow? _current;

    private bool _isActive = true;
    private bool _isDark;
    private IntPtr _iconHandle;
    private bool _iconSet;
    private readonly Dictionary<string, Page> _pageCache = new();

    private HomePage? _homePage;
    private SettingsPage? _settingsPage;

    public MainWindow()
    {
        _current = this;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        try { SystemBackdrop = new MicaBackdrop(); } catch { }

        // 从工具目录动态生成导航菜单项
        foreach (var tool in ToolCatalog.Tools)
        {
            NavView.MenuItems.Add(new NavigationViewItem
            {
                Content = tool.Title,
                Tag = tool.Id,
                Icon = new FontIcon { Glyph = tool.Glyph }
            });
        }

        ContentFrame.Navigate(typeof(HomePage));
        _homePage = ContentFrame.Content as HomePage;
        NavView.SelectedItem = NavView.MenuItems[0];

        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionString = ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "";
        TitleText.Text = $"TOfont {versionString}";

        if (Content is FrameworkElement root)
            root.ActualThemeChanged += (_, _) => { RefreshTheme(); UpdateTitleBar(); };

#if DEBUG
        // Debug 专属：按住 Ctrl+Alt 再左键点击，把控件类型链复制到剪贴板
        if (Content is FrameworkElement debugRoot)
        {
            debugRoot.AddHandler(UIElement.PointerPressedEvent,
                new PointerEventHandler(OnDebugInspect), true);
        }
#endif
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
        Closed += (_, _) => DestroyIconHandle();
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

#if DEBUG
    /// <summary>
    /// Debug 专属控件检视：按住 Ctrl+Alt 左键点击，把控件类型链 + 运行时实际属性复制到剪贴板。
    /// </summary>
    private void OnDebugInspect(object sender, PointerRoutedEventArgs e)
    {
        if ((e.KeyModifiers & VirtualKeyModifiers.Control) == 0 ||
            (e.KeyModifiers & VirtualKeyModifiers.Menu) == 0)
            return;

        if (e.OriginalSource is not DependencyObject source) return;

        var sb = new StringBuilder();
        var node = source;
        var depth = 0;
        while (node != null && depth < 15)
        {
            var typeName = node.GetType().Name;
            var info = new StringBuilder(typeName);

            if (node is FrameworkElement fe)
            {
                var name = fe.Name;
                if (!string.IsNullOrEmpty(name)) info.Append($"  x:Name=\"{name}\"");

                // 运行时实际布局属性：真实尺寸、位置、可见性
                var pos = fe.TransformToVisual(null)?.TransformPoint(new Windows.Foundation.Point(0, 0));
                info.Append($"  size=({fe.ActualWidth:0.#}x{fe.ActualHeight:0.#})");
                if (pos != null)
                    info.Append($"  pos=({pos.Value.X:0.#},{pos.Value.Y:0.#})");
                info.Append($"  vis={fe.Visibility}");
                info.Append($"  opacity={fe.Opacity:0.##}");
            }
            else
            {
                var autoName = AutomationProperties.GetName(node);
                if (!string.IsNullOrEmpty(autoName)) info.Append($"  AutomationName=\"{autoName}\"");
            }

            sb.AppendLine($"{new string(' ', depth * 2)}{info}");
            node = VisualTreeHelper.GetParent(node);
            depth++;
        }

        var text = sb.ToString().TrimEnd();
        try
        {
            var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
            pkg.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
            Debug.WriteLine($"[DEBUG] 控件信息已复制到剪贴板:\n{text}");
        }
        catch { }

        e.Handled = true;
    }
#endif

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
                case "settings":
                    _settingsPage ??= new SettingsPage();
                    target = _settingsPage;
                    break;
                default:
                    // 工具页：从 ToolCatalog 查找，按需懒加载并缓存实例
                    var tool = ToolCatalog.FindById(tag);
                    if (tool != null)
                    {
                        if (!_pageCache.TryGetValue(tool.Id, out var page))
                        {
                            page = (Page)Activator.CreateInstance(tool.PageType)!;
                            _pageCache[tool.Id] = page;
                        }
                        target = page;
                    }
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
            // 只设置一次，避免每次窗口激活都创建新的 HICON 导致 GDI 句柄泄漏
            if (_iconSet) return;
            var hwnd = GetHandle();
            if (hwnd == IntPtr.Zero) return;
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png");
            if (!System.IO.File.Exists(iconPath)) return;
            using var bitmap = new System.Drawing.Bitmap(iconPath);
            _iconHandle = bitmap.GetHicon();
            const uint WM_SETICON = 0x0080;
            SendMessage(hwnd, WM_SETICON, 0, _iconHandle);
            SendMessage(hwnd, WM_SETICON, 1, _iconHandle);
            _iconSet = true;
        }
        catch { }
    }

    private void DestroyIconHandle()
    {
        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, int wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

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
