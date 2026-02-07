using System.Windows;
using MkvProcessor.Services;

namespace MkvProcessor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppConfiguration.Initialize();
        var mainWindow = new MainWindow();
        mainWindow.Show();
    }
}
