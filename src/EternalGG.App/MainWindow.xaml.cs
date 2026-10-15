using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using EternalGG.App.ViewModels;

namespace EternalGG.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => LimitPhysicalSize();
        DpiChanged += (_, _) => LimitPhysicalSize();
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.SettingsOpen) && viewModel.SettingsOpen)
                Dispatcher.InvokeAsync(() => SettingsPanel.BringIntoView());
        };
        timer.Tick += async (_, _) => await viewModel.TickAsync();
        Loaded += async (_, _) => { await viewModel.TickAsync(); timer.Start(); };
        Closed += (_, _) => { timer.Stop(); viewModel.Dispose(); };
    }

    private void ApiKeyChanged(object sender, RoutedEventArgs e) => viewModel.ApiKey = ((PasswordBox)sender).Password;

    private void SearchClick(object sender, RoutedEventArgs e)
    {
        viewModel.SettingsOpen = false;
        FindNameInput(this)?.Focus();
    }

    private static TextBox? FindNameInput(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox box) { box.BringIntoView(); return box; }
            if (FindNameInput(child) is { } found) return found;
        }

        return null;
    }

    private void LimitPhysicalSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        MinWidth = Math.Min(980, 1248 / dpi.DpiScaleX);
        MinHeight = Math.Min(760, 1248 / dpi.DpiScaleY);
        MaxWidth = 1248 / dpi.DpiScaleX;
        MaxHeight = 1248 / dpi.DpiScaleY;
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
    }
}
