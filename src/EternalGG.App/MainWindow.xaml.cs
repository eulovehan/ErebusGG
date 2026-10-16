using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using EternalGG.App.ViewModels;

namespace EternalGG.App;

/** 메인 창 이벤트와 화면 연결 */
public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    /** 화면 초기화 및 상태 갱신 타이머 연결 */
    public MainWindow() : this(new MainViewModel(), true) { }

    /** 상태 모델과 게임 감시의 명시적 연결 */
    public MainWindow(MainViewModel model, bool monitorGame)
    {
        viewModel = model;
        InitializeComponent();
        SourceInitialized += (_, _) => LimitPhysicalSize();
        DpiChanged += (_, _) => LimitPhysicalSize();
        DataContext = viewModel;
        viewModel.AttentionRequested += (_, _) => BringForwardOnce();

        /** 탐색 이동 후 스크롤과 검색 입력 위치 갱신 */
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.DeveloperMode) && !viewModel.DeveloperMode)
                ApiKeyBox.Clear();
            if (args.PropertyName != nameof(MainViewModel.CurrentPage)) return;
            Dispatcher.InvokeAsync(() =>
            {
                MainScroll.ScrollToTop();
                if (viewModel.SearchOpen) SearchInput.Focus();
            });
        };

        /** 주기 갱신 시작 및 창 종료 시 자원 정리 */
        timer.Tick += async (_, _) => await viewModel.TickAsync();
        if (monitorGame) Loaded += async (_, _) => { await viewModel.TickAsync(); timer.Start(); };
        Closed += (_, _) => { timer.Stop(); viewModel.Dispose(); };
    }

    /** 최소화 복구와 한 번의 창 전면 표시 */
    private void BringForwardOnce()
    {
        if (!IsLoaded) return;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        var previousTopmost = Topmost;
        try { Topmost = true; Activate(); }
        finally { Topmost = previousTopmost; }
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
