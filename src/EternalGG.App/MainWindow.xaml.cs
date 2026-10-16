using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using EternalGG.App.ViewModels;

namespace EternalGG.App;

/** 메인 창 이벤트와 화면 연결 */
public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    /** 화면 초기화 및 상태 갱신 타이머 연결 */
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => LimitPhysicalSize();
        DpiChanged += (_, _) => LimitPhysicalSize();
        DataContext = viewModel;

        /** 설정 표시 시 입력 영역으로 이동 */
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.SettingsOpen) && viewModel.SettingsOpen)
                Dispatcher.InvokeAsync(() => SettingsPanel.BringIntoView());
        };

        /** 주기 갱신 시작 및 창 종료 시 자원 정리 */
        timer.Tick += async (_, _) => await viewModel.TickAsync();
        Loaded += async (_, _) => { await viewModel.TickAsync(); timer.Start(); };
        Closed += (_, _) => { timer.Stop(); viewModel.Dispose(); };
    }

    /** 창 최소화 */
    private void MinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    /** 창 최대화와 이전 크기 복원 */
    private void MaximizeRestoreClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    /** 창 닫기 */
    private void CloseClick(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    /** 입력한 API 키의 메모리 상태 반영 */
    private void ApiKeyChanged(object sender, RoutedEventArgs e) => viewModel.ApiKey = ((PasswordBox)sender).Password;

    /** 직접 검색용 닉네임 입력란 이동 */
    private void SearchClick(object sender, RoutedEventArgs e)
    {
        viewModel.SettingsOpen = false;
        FindNameInput(this)?.Focus();
    }

    /** 화면 트리의 첫 닉네임 입력란 탐색 */
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

    /** 화면 배율별 물리 창 크기의 최대 1248픽셀 제한 */
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
