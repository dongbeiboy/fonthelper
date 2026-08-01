using Microsoft.UI.Xaml.Controls;
using TOfont.WinUI.Framework;

namespace TOfont.WinUI.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        ToolGrid.ItemsSource = ToolCatalog.Tools.Where(t => t.ShowInHome).ToList();
    }

    private void OnToolClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ToolDescriptor tool)
            MainWindow.NavigateTo(tool.Id);
    }
}
