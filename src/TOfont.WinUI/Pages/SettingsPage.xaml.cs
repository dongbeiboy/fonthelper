using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Net.Http;
using System.Text.Json;

namespace TOfont.WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _loaded;

    public SettingsPage()
    {
        InitializeComponent();
        ScanModeCombo.SelectedIndex = AppSettings.ScanMode;
        MsbFirstChk.IsChecked = AppSettings.MsbFirst;
        LitIs1Chk.IsChecked = AppSettings.LitIs1;
        UseHexChk.IsChecked = AppSettings.UseHex;
        AutoScrollChk.IsChecked = AppSettings.ShellAutoScroll;

        // CLI 服务子页面
        CliToggle.IsOn = AppSettings.CliEnabled;
        CliPortBox.Value = AppSettings.CliPort;
        CliPortBox.IsEnabled = !AppSettings.CliEnabled;
        UpdateCliStatus();

        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionString = ver != null ? $"版本 v{ver.Major}.{ver.Minor}.{ver.Build}" : "";
        VersionText.Text = versionString;

        _loaded = true;
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsPanel == null || SerialPanel == null || CliPanel == null || AboutPanel == null) return;
        if (NavList.SelectedItem is ListViewItem item && item.Tag is string tag)
        {
            SettingsPanel.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
            SerialPanel.Visibility = tag == "serial" ? Visibility.Visible : Visibility.Collapsed;
            CliPanel.Visibility = tag == "cli" ? Visibility.Visible : Visibility.Collapsed;
            AboutPanel.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        AppSettings.ScanMode = ScanModeCombo.SelectedIndex;
        AppSettings.MsbFirst = MsbFirstChk.IsChecked == true;
        AppSettings.LitIs1 = LitIs1Chk.IsChecked == true;
        AppSettings.UseHex = UseHexChk.IsChecked == true;
        AppSettings.ShellAutoScroll = AutoScrollChk.IsChecked == true;
    }

    // ========== CLI 服务 ==========

    private void OnCliToggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        AppSettings.CliEnabled = CliToggle.IsOn;
        CliPortBox.IsEnabled = !CliToggle.IsOn;
        CliErrorText.Visibility = Visibility.Collapsed;

        if (CliToggle.IsOn)
        {
            var error = App.StartCliServer();
            if (error.Length > 0)
            {
                AppSettings.CliEnabled = false;
                CliToggle.IsOn = false;
                CliPortBox.IsEnabled = true;
                CliErrorText.Text = $"启动失败: {error}";
                CliErrorText.Visibility = Visibility.Visible;
                UpdateCliStatus();
                return;
            }
        }
        else
        {
            App.StopCliServer();
        }
        UpdateCliStatus();
    }

    private void OnCliPortChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_loaded || double.IsNaN(args.NewValue)) return;
        AppSettings.CliPort = (int)args.NewValue;
    }

    private void UpdateCliStatus()
    {
        if (CliStatusText == null) return;
        if (App.CliServer?.IsRunning == true)
            CliStatusText.Text = $"监听 http://127.0.0.1:{App.CliServer.Port}";
        else
            CliStatusText.Text = "未运行";
    }

    // ========== 检查更新 ==========

    /// <summary>当前版本字符串，如 "v1.0.2"。</summary>
    private static string CurrentVersion()
    {
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v0.0.0";
    }

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        CheckUpdateBtn.IsEnabled = false;
        UpdateStatusText.Text = "检查中...";
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TOfont");
            http.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var json = await http.GetStringAsync(
                "https://api.github.com/repos/dongbeiboy/fonthelper/releases/latest");

            string tag = "", url = "";
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("tag_name", out var t)) tag = t.GetString() ?? "";
                if (root.TryGetProperty("html_url", out var u)) url = u.GetString() ?? "";
            }

            var cur = CurrentVersion();
            if (string.IsNullOrEmpty(tag))
            {
                UpdateStatusText.Text = "获取版本信息失败";
                return;
            }

            if (CompareVersion(tag, cur) > 0)
            {
                UpdateStatusText.Text = $"发现新版本 {tag}（当前 {cur}）";
                var dlg = new ContentDialog
                {
                    Title = "发现新版本",
                    Content = $"最新版本 {tag}，当前 {cur}。\n是否前往 GitHub 下载？",
                    PrimaryButtonText = "去下载",
                    CloseButtonText = "取消",
                    XamlRoot = XamlRoot
                };
                var res = await dlg.ShowAsync();
                if (res == ContentDialogResult.Primary && !string.IsNullOrEmpty(url))
                {
                    try { await Windows.System.Launcher.LaunchUriAsync(new Uri(url)); }
                    catch { UpdateStatusText.Text = "无法打开浏览器"; }
                }
            }
            else
            {
                UpdateStatusText.Text = $"已是最新版本（{cur}）";
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"检查失败：{ex.Message}";
        }
        finally
        {
            CheckUpdateBtn.IsEnabled = true;
        }
    }

    /// <summary>比较两个版本字符串（如 v1.2.3），返回 &gt;0 表示 a 更新。</summary>
    private static int CompareVersion(string a, string b)
    {
        var pa = ParseVersion(a);
        var pb = ParseVersion(b);
        for (var i = 0; i < 3; i++)
        {
            if (pa[i] != pb[i]) return pa[i].CompareTo(pb[i]);
        }
        return 0;

        static int[] ParseVersion(string v)
        {
            var s = v.TrimStart('v', 'V');
            var parts = s.Split('.');
            var arr = new int[3];
            for (var i = 0; i < 3 && i < parts.Length; i++)
                int.TryParse(parts[i], out arr[i]);
            return arr;
        }
    }
}
