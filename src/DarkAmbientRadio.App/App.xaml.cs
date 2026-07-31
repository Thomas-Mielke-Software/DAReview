using System.Windows;
using DarkAmbientRadio.App.ViewModels;
using DarkAmbientRadio.Core.Config;

namespace DarkAmbientRadio.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var config = AppConfig.Load();
        var window = new MainWindow { DataContext = new MainViewModel(config) };
        MainWindow = window;
        window.Show();
    }
}
