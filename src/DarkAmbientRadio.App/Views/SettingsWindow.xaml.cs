using System.Windows;
using DarkAmbientRadio.App.ViewModels;
using DarkAmbientRadio.Core.Config;

namespace DarkAmbientRadio.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(AppConfig config)
    {
        InitializeComponent();
        _viewModel = new SettingsViewModel(config);
        DataContext = _viewModel;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _viewModel.ApplyTo();
        DialogResult = true;
    }
}
