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
using EternalGG.App.Services;

/** 외부 통신 없는 화면 상태와 배치 검증 */
internal static class Program
{
    private static int count;

    /** 실제 화면 자원과 검증용 상태 구성 */
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--attention-preview")) { RunAttentionPreview(); return; }
        if (args.FirstOrDefault() == "--ocr-image") { RunOcrImage(args); return; }
        if (args.FirstOrDefault() == "--ocr-languages") { RunOcrLanguages(args); return; }
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
        card.SetRegisteredName("등록이름");
        Assert(!card.HasConfirmedName, "registered name is not a screen confirmation");
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

        /** 일부 이름 누락 시 직접 입력 복구와 본인 이름 보호 검증 */
        using var captureVm = new MainViewModel { SelfNickname = "" };
        IReadOnlyList<OcrCandidate> partialNames = [new(0, "첫이름", 0.93f), new(2, "끝이름", 0.92f)];
        for (var frame = 0; frame < 3; frame++) captureVm.ApplyNameFrame(partialNames);
        Assert(captureVm.PlayerCount == "2 / 3" && captureVm.VisiblePlayers.Count == 3 && !captureVm.Players[1].HasName, "missing slot remains available for manual recovery");
        window.DataContext = captureVm;
        Layout(root, 980, 760);
        Audit(root, 980);
        if (output != null) Render(root, Path.Combine(output, "partial-roster-recovery.png"), 980, 760);
        window.DataContext = vm;
        captureVm.Players[1].Nickname = "수정이름";
        for (var frame = 0; frame < 3; frame++) captureVm.ApplyNameFrame([new(1, "다른이름", 0.95f)]);
        Assert(captureVm.Players[1].Nickname == "수정이름", "manual recovery survives later screen candidates");
        using var ownVm = new MainViewModel { SelfNickname = "등록본인" };
        ownVm.Players[0].SetRegisteredName(ownVm.SelfNickname);
        for (var frame = 0; frame < 3; frame++) ownVm.ApplyNameFrame([new(0, "다른이름", 0.93f)]);
        Assert(ownVm.Players[0].Nickname == "등록본인" && !ownVm.Players[0].ScreenVerified, "conflicting detection cannot replace registered identity");
        for (var frame = 0; frame < 3; frame++) ownVm.ApplyNameFrame([new(0, "등록본인", 0.93f)]);
        Assert(ownVm.Players[0].ScreenVerified, "registered identity needs independent screen evidence");
        ownVm.Match.Reset();
        foreach (var player in ownVm.Players) player.Reset();
        Assert(ownVm.VisiblePlayers.Count == 0 && ownVm.EmptyTeam, "inactive reset roster returns to one empty state");
        PresentationTraceSources.DataBindingSource.Flush();
        Assert(errors.ToString().Length == 0, "no runtime binding errors");
        window.Close();
        Console.WriteLine(count + " UI checks passed. Offscreen fixtures only.");
    }

    /** 외부 파일을 보관하지 않는 실제 인식 이미지 회귀 검증 */
    private static void RunOcrImage(string[] args)
    {
        if (args.Length < 2) throw new ArgumentException("--ocr-image path [expectedName ...]");
        var expected = args.Skip(2).ToArray();
        var watch = Stopwatch.StartNew();
        using var image = new System.Drawing.Bitmap(args[1]);
        using var ocr = new OcrService();
        using var vm = new MainViewModel { SelfNickname = "" };
        for (var frame = 0; frame < NicknameConsensus.RequiredFrames; frame++)
        {
            var candidates = ocr.Read(image, new AppSettings().SelectionRegions);
            Console.WriteLine(ocr.Diagnostics);
            Assert(candidates.Select(c => c.Name).SequenceEqual(expected), "real OCR candidates frame " + frame);
            vm.ApplyNameFrame(candidates);
            if (frame < NicknameConsensus.RequiredFrames - 1) Assert(vm.Players.All(p => !p.ScreenVerified), "no premature frame confirmation");
        }

        Assert(vm.Players.Take(expected.Length).Select(p => p.Nickname).SequenceEqual(expected), "image to displayed roster");
        Assert(vm.Players.Take(expected.Length).All(p => p.ScreenVerified), "screen confirmation flags");
        if (expected.Length == 0) Assert(vm.Players.All(p => !p.HasName), "negative screenshot never becomes a roster");
        using var blank = new System.Drawing.Bitmap(image.Width, image.Height);
        Assert(ocr.Read(blank, new AppSettings().SelectionRegions).Count == 0, "blank frame has no names");

        /** 이름 띠가 없거나 왼쪽에 놓인 문자 오확정 제외 */
        using var wrongPlace = new System.Drawing.Bitmap(image.Width, image.Height);
        var region = new AppSettings().SelectionRegions[0];
        var box = new System.Drawing.Rectangle((int)(region.X * image.Width), (int)(region.Y * image.Height), (int)(region.Width * image.Width), (int)(region.Height * image.Height));
        using (var graphics = System.Drawing.Graphics.FromImage(wrongPlace))
        {
            graphics.Clear(System.Drawing.Color.FromArgb(20, 23, 30));
            using var font = new System.Drawing.Font("Malgun Gothic", 10);
            graphics.DrawString("바냐", font, System.Drawing.Brushes.White, box.Left + 3, box.Top + 2);
        }

        Assert(ocr.Read(wrongPlace, [region]).Count == 0, "non-nameplate text is not a player");
        using (var graphics = System.Drawing.Graphics.FromImage(wrongPlace))
            graphics.FillRectangle(System.Drawing.Brushes.LimeGreen, box.Left, box.Bottom - 2, box.Width, 2);
        Assert(ocr.Read(wrongPlace, [region]).Count == 0, "left-aligned label is not a right-aligned nickname");
        Console.WriteLine($"{count} image checks passed in {watch.ElapsedMilliseconds}ms. Static image only, no game or HTTP.");
    }

    /** 다국어와 숫자 및 구분선이 포함된 합성 이름 띠 회귀 검증 */
    private static void RunOcrLanguages(string[] args)
    {
        using var ocr = new OcrService();
        var failures = new List<string>();
        var samples = new[]
        {
            (Font: "Malgun Gothic", Names: new[] { "이하링", "달빛123", "하늘소리7" }),
            (Font: "Yu Gothic", Names: new[] { "さくら123", "カタカナ42", "あおぞら7" }),
            (Font: "Microsoft YaHei", Names: new[] { "星空123", "测试玩家8", "永恒回归9" }),
            (Font: "Microsoft JhengHei", Names: new[] { "星光123", "測試玩家8", "永恆回歸9" }),
            (Font: "Segoe UI", Names: new[] { "MikonS2", "momoka", "Player123" }),
            (Font: "Malgun Gothic", Names: new[] { "봄Sakura7", "さくらStar2", "星光GG123" }),
            (Font: "Segoe UI", Names: new[] { "123456", "20261018", "987654" })
        };
        foreach (var sample in samples)
        {
            using var image = new System.Drawing.Bitmap(1920, 1080);
            using (var graphics = System.Drawing.Graphics.FromImage(image))
            using (var font = new System.Drawing.Font(sample.Font, 16, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel))
            using (var format = (System.Drawing.StringFormat)System.Drawing.StringFormat.GenericTypographic.Clone())
            {
                Assert(font.FontFamily.GetName(0x0409) == sample.Font, "requested fixture font available " + sample.Font);
                graphics.Clear(System.Drawing.Color.FromArgb(35, 38, 43));
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                format.Alignment = System.Drawing.StringAlignment.Far;
                var regions = new AppSettings().SelectionRegions;
                for (var slot = 0; slot < regions.Count; slot++)
                {
                    var r = regions[slot];
                    var box = new System.Drawing.Rectangle((int)(r.X * image.Width), (int)(r.Y * image.Height), (int)(r.Width * image.Width), (int)(r.Height * image.Height));
                    graphics.FillRectangle(System.Drawing.Brushes.DeepSkyBlue, box.Left, box.Bottom - 2, box.Width, 2);
                    graphics.FillRectangle(System.Drawing.Brushes.White, box.Left + 2, box.Top + 3, 1, 13);
                    graphics.DrawString(sample.Names[slot], font, System.Drawing.Brushes.White, new System.Drawing.PointF(box.Right - 3, box.Top - 2), format);
                }
            }

            if (args.Length > 1)
            {
                Directory.CreateDirectory(args[1]);
                image.Save(Path.Combine(args[1], sample.Names[0] + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            }

            var result = ocr.Read(image, new AppSettings().SelectionRegions);
            Console.WriteLine(ocr.Diagnostics);
            if (!result.Select(c => c.Name).SequenceEqual(sample.Names)) failures.Add(string.Join(" / ", sample.Names));
            else Assert(true, "actual OCR synthetic names " + string.Join(" / ", sample.Names));
        }

        Assert(failures.Count == 0, "all language fixtures " + string.Join("; ", failures));
        Console.WriteLine(count + " multilingual checks passed. Synthetic fixtures only, no game or HTTP.");
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

