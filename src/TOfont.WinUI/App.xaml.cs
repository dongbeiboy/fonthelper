using Microsoft.UI.Xaml;

namespace TOfont.WinUI;

public partial class App : Application
{
    private Window _window = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
