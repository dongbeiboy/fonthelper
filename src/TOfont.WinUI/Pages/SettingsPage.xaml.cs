using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
        _loaded = true;
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        AppSettings.ScanMode = ScanModeCombo.SelectedIndex;
        AppSettings.MsbFirst = MsbFirstChk.IsChecked == true;
        AppSettings.LitIs1 = LitIs1Chk.IsChecked == true;
        AppSettings.UseHex = UseHexChk.IsChecked == true;
    }
}
