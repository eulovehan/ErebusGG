using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EternalGG.App;
using EternalGG.App.ViewModels;
using EternalGG.Core;

/** 외부 통신 없는 화면 상태와 배치 검증 */
internal static class Program
{
    private static int count;

    /** 실제 화면 자원과 검증용 상태 구성 */
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--attention-preview")) { RunAttentionPreview(); return; }
        var app = new App();
        app.InitializeComponent();
        var errors = new StringWriter();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(errors));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var window = new MainWindow();
        var vm = (MainViewModel)window.DataContext;
        var root = (FrameworkElement)window.Content;
        var output = args.FirstOrDefault();
        if (output != null) Directory.CreateDirectory(output);

        /** 기본 화면과 일반 설정의 내부 정보 노출 검증 */
        foreach (var page in new[] { "match", "search", "settings", "help" })
        {
            vm.NavigateCommand.Execute(page);
            foreach (var size in new[] { (W: 1160, H: 910), (W: 980, H: 760), (W: 832, H: 832), (W: 1248, H: 1248) })
            {
                Layout(root, size.W, size.H);
                Audit(root, size.W);
                var text = string.Join(" ", Descendants(root).OfType<TextBlock>().Where(Visible).Select(t => t.Text));
                Assert(!new[] { "API", "OCR", "로그", "ADVERTISEMENT", "시즌 ID" }.Any(text.Contains), page + " default disclosure " + size.W);
                if (output != null) Render(root, Path.Combine(output, page + "-" + size.W + ".png"), size.W, size.H);
            }
        }

        /** 빈 검색과 조회 불가 상태의 현재 팀 보존 검증 */
        vm.NavigateCommand.Execute("search");
        vm.SearchNickname = "";
        vm.SearchCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert(!vm.SearchHasResult && vm.SearchMessage.Contains("입력"), "empty search recovery");
        vm.Players[0].SetDetectedName("검증팀원");
        vm.SearchNickname = "검증검색";
        Assert(!vm.SearchHasResult, "typing does not claim a result");
        vm.SearchCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert(vm.SearchHasResult && vm.SearchPlayer.Nickname == "검증검색" && vm.Players[0].Nickname == "검증팀원", "search does not mutate roster");
        Assert(vm.SearchOpen && !vm.DeveloperMode && !vm.SearchPlayer.HasStats && vm.SearchPlayer.Status.Contains("사용할 수 없어요"), "unavailable service stays on task");
        Assert(!vm.ShowSearchService, "search failure explanation appears only once");
        vm.SearchNickname = "다른검색";
        Assert(!vm.SearchHasResult && vm.ShowSearchService, "edited query hides previous result");

        /** 직접 수정한 이름과 이전 데이터 구분 검증 */
        var card = vm.Players[0];
        card.Nickname = "직접수정";
        Assert(!card.SetDetectedName("자동후보") && card.Nickname == "직접수정", "manual correction survives detection");
        var revision = card.Revision;
        card.Nickname = "다른이름";
        card.Nickname = "";
        Assert(vm.VisiblePlayers.Contains(card), "cleared nickname retains editable card");
        Assert(!card.SetDetectedName("자동후보"), "editing empty name still blocks automatic overwrite");
        card.Nickname = "직접수정";
        Assert(card.Revision > revision, "name edit and revert invalidates response");
        var fixture = new PlayerStats("직접수정", "fixture", 20, 4, 1200, 3.5, "2위 / 4위", DateTimeOffset.Now);
        card.Apply(new(fixture, "전적 조회 완료"));
        card.Apply(new(null, "잠시 후 다시 조회해 주세요"));
        Assert(card.HasStats && card.Games == "20" && card.FetchedAt.StartsWith("이전 조회 결과"), "failed refresh preserves marked previous result");
        vm.Ranked = !vm.Ranked;
        Assert(!card.HasStats, "mode change separates previous statistics");
        card.Reset();
        card.SetDetectedName("등록이름");
        card.Source = "등록한 이름 / 확인 필요";
        Assert(card.SetDetectedName("등록이름") && card.Source == "자동 확인", "registered name still requires screen confirmation");
        card.Reset();
        Assert(!card.UserEdited && card.SetDetectedName("새팀원"), "new match accepts detection");

        /** 검증용 세 카드와 긴 이름의 배치 검증 */
        foreach (var player in vm.Players)
        {
            player.Nickname = "검증용긴닉네임123456";
            player.Apply(new(fixture, "전적 조회 완료"));
        }
        vm.Players[1].Apply(new(null, "잠시 후 다시 조회해 주세요"));
        vm.Players[2].ClearStats();
        vm.Players[2].Status = "선택 조건의 전적이 없습니다";
        vm.NavigateCommand.Execute("match");
        foreach (var size in new[] { (W: 980, H: 760), (W: 832, H: 832), (W: 1160, H: 910) })
        {
            Layout(root, size.W, size.H);
            Audit(root, size.W);
            if (output != null) Render(root, Path.Combine(output, "fixture-team-" + size.W + ".png"), size.W, size.H);
        }
        Assert(vm.VisiblePlayers.Count() == 3, "three player fixture");
        vm.Match.Apply("[GlobalUserData:SetMatchingMode:142] Invoked: Test");
        vm.Players[0].Nickname = "AI본인";
        Assert(vm.VisiblePlayers.Count() == 1, "AI excludes two bots");
        vm.SeasonInput = "33";
        vm.DeveloperMode = true;
        vm.ApiKey = "fixture-only";
        vm.DeveloperMode = false;
        Assert(vm.ApiKey == "", "leaving developer mode removes in-memory key");

        /** 설정에 따른 알림 요청과 사용자 안내 검증 */
        var alerts = 0;
        vm.AttentionRequested += (_, _) => alerts++;
        vm.BringForward = false;
        var now = DateTimeOffset.Now;
        vm.ObserveGameAttention(true, MatchPhase.Lobby, 0, false, now);
        Assert(alerts == 0 && vm.LatestNotice.Contains("게임 연결"), "disabled window setting preserves notice without focus request");
        vm.BringForward = true;
        vm.ObserveGameAttention(true, MatchPhase.Matched, 1, false, now);
        vm.ObserveGameAttention(true, MatchPhase.Matched, 1, true, now.AddSeconds(2));
        vm.ObserveGameAttention(true, MatchPhase.Loading, 1, true, now.AddSeconds(3));
        Assert(alerts == 1 && vm.MatchOpen && vm.LatestNotice.Contains("새 매칭"), "single match window request navigates to current match");
        Assert(!window.IsVisible, "offscreen checks never show product window");
        PresentationTraceSources.DataBindingSource.Flush();
        Assert(errors.ToString().Length == 0, "no runtime binding errors");
        window.Close();
        Console.WriteLine(count + " UI checks passed. Offscreen fixtures only.");
    }

    /** 게임 감시 없는 실제 창 알림 검수 도구 */
    private static void RunAttentionPreview()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/EternalGG;component/Themes/Neutral.xaml", UriKind.Relative) });
        var vm = new MainViewModel { BringForward = true, GameStatus = "검수용 상태 / 실제 게임 조작 없음" };
        var window = new MainWindow(vm, false) { Title = "[검수] EternalGG / 게임 감시 없음" };
        var controls = new Window
        {
            Title = "EternalGG / UI 검수 컨트롤", Width = 380, Height = 300,
            Background = (Brush)app.FindResource("BackgroundBrush"), Foreground = Brushes.White,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "실제 게임 없는 알림 검수", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        var connect = new Button { Content = "연결 알림 재현", Margin = new Thickness(0, 0, 0, 12) };
        var matched = new Button { Content = "매칭 알림 재현" };
        var generation = 0;
        connect.Click += (_, _) =>
        {
            vm.PhaseTitle = "게임 연결 알림 검수";
            vm.PhaseDetail = "검수용 신호 / 실제 게임과 연결하지 않은 창입니다";
            vm.ObserveGameAttention(false, MatchPhase.Lobby, generation, false, DateTimeOffset.Now);
            vm.ObserveGameAttention(true, MatchPhase.Lobby, generation, false, DateTimeOffset.Now);
        };
        matched.Click += (_, _) =>
        {
            vm.PhaseTitle = "매칭 알림 검수";
            vm.PhaseDetail = "검수용 신호 / 실제 매칭은 시작하지 않았습니다";
            generation++;
            var now = DateTimeOffset.Now;
            vm.ObserveGameAttention(true, MatchPhase.Matched, generation, false, now);
            vm.ObserveGameAttention(true, MatchPhase.Matched, generation, true, now.AddSeconds(2));
        };
        panel.Children.Add(connect);
        panel.Children.Add(matched);
        panel.Children.Add(new TextBlock { Text = "이 도구는 게임 창, 캡처, 로그를 사용하지 않습니다", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) });
        controls.Content = panel;
        window.Closed += (_, _) => controls.Close();
        window.Loaded += (_, _) => controls.Show();
        app.Run(window);
    }

    /** 조건별 검증 실패 중단 */
    private static void Assert(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine("PASS " + label);
        count++;
    }

    /** 현재 페이지의 가시성 확인 */
    private static bool Visible(DependencyObject element)
    {
        for (var node = element; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement ui && ui.Visibility != Visibility.Visible) return false;
        return true;
    }

    /** 고정 크기의 화면 배치 */
    private static void Layout(FrameworkElement root, int width, int height)
    {
        root.Width = width;
        root.Height = height;
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        root.UpdateLayout();
    }

    /** 조작 요소의 가로 넘침 검증 */
    private static void Audit(FrameworkElement root, int width)
    {
        foreach (var element in Descendants(root).OfType<FrameworkElement>())
        {
            if (!Visible(element) || element.ActualWidth == 0 || element is not (Button or TextBox or PasswordBox or CheckBox)) continue;
            var point = element.TransformToAncestor(root).Transform(new Point());
            if (point.X < -1 || point.X + element.ActualWidth > width + 1)
                throw new InvalidOperationException("Control overflow: " + element.GetType().Name);
        }

        Assert(true, "control bounds " + width);
    }

    /** 화면 트리의 하위 요소 열거 */
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var next in Descendants(child)) yield return next;
        }
    }

    /** 검증용 화면 이미지 생성 */
    private static void Render(FrameworkElement root, string path, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        png.Save(output);
    }
}

