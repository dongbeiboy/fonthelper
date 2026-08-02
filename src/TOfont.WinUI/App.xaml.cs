using Microsoft.UI.Xaml;
using TOfont.WinUI.Services;

namespace TOfont.WinUI;

public partial class App : Application
{
    private Window _window = null!;

    /// <summary>CLI 模式 HTTP 服务（全局单例）。</summary>
    public static CliServer? CliServer { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try
            {
                var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n\n";
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log"), msg);
                Console.Error.WriteLine(msg);
            }
            catch { }
        };
    }

    /// <summary>启动 CLI 服务。返回空串表示成功，否则返回错误信息。</summary>
    public static string StartCliServer()
    {
        StopCliServer();
        CliServer = new CliServer(AppSettings.CliPort);
        var error = CliServer.Start();
        if (error.Length > 0)
        {
            CliServer.Dispose();
            CliServer = null;
        }
        return error;
    }

    public static void StopCliServer()
    {
        if (CliServer != null)
        {
            CliServer.Dispose();
            CliServer = null;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
