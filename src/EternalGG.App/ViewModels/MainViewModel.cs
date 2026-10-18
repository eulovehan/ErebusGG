using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EternalGG.App.Services;
using EternalGG.Core;
using Tesseract;

namespace EternalGG.App.ViewModels;

/** 매칭 감지와 이름 인식의 화면 상태 관리 */
public partial class MainViewModel : ObservableObject, IDisposable
{
    public AppSettings Settings { get; } = AppSettings.Load();
    public ObservableCollection<PlayerCard> Players { get; } = [new() { Label = "나" }, new() { Label = "팀원 2" }, new() { Label = "팀원 3" }];
    public ObservableCollection<string> Events { get; } = [];
    public MatchState Match { get; } = new();
    private readonly LogMonitor logs = new();
    private readonly OcrService ocr = new();
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly CancellationTokenSource lifetime = new();
    private readonly NicknameConsensus names = new();
    private readonly GameAttention attention = new();
    public event EventHandler? AttentionRequested;
    private DateTime captureUntil;
    private DateTime lastCapture;
    private DateTime manualCaptureUntil;
    private bool ticking;
    private bool disposed;
    private bool wasRunning;
    private bool initial = true;

    /** 화면 바인딩용 연결 상태와 사용자 입력 */
    [ObservableProperty] private string gameStatus = "게임 연결 확인 중";
    [ObservableProperty] private string phaseTitle = "다음 매칭을 기다리는 중";
    [ObservableProperty] private string phaseDetail = "게임과 연결되면 팀원 이름을 자동으로 확인합니다.";
    [ObservableProperty] private string modeLabel = "자동 감지";
    [ObservableProperty] private string logStatus = "확인 중";
    [ObservableProperty] private string ocrStatus = "대기";
    [ObservableProperty] private string ocrDiagnostic = "아직 읽지 않음";
    [ObservableProperty] private string lastUpdated = "아직 조회하지 않음";
    [ObservableProperty] private string playerCount = "0 / 3";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowSearchService))] private string apiKey = "";
    [ObservableProperty] private string seasonInput = "";
    [ObservableProperty] private bool ranked;
    [ObservableProperty] private bool autoDetect = true;
    [ObservableProperty] private bool bringForward = true;
    [ObservableProperty] private string latestNotice = "";
    [ObservableProperty] private string currentPage = "match";
    [ObservableProperty] private bool developerMode;
    [ObservableProperty] private bool isLookupBusy;
    [ObservableProperty] private string searchNickname = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowSearchService))] private bool searchHasResult;
    [ObservableProperty] private string searchMessage = "닉네임을 입력해 전적을 확인하세요";
    [ObservableProperty] private string nameMessage = "";
    public PlayerCard SearchPlayer { get; } = new() { Label = "검색 결과", IsReadOnly = true };
    public ObservableCollection<PlayerCard> VisiblePlayers { get; } = [];
    public bool HasPlayers => VisiblePlayers.Any();
    public bool EmptyTeam => !HasPlayers;
    public bool MatchOpen => CurrentPage == "match";
    public bool SearchOpen => CurrentPage == "search";
    public bool SettingsOpen { get => CurrentPage == "settings"; set { if (value) CurrentPage = "settings"; else if (SettingsOpen) CurrentPage = "match"; } }
    public string PageTitle => CurrentPage switch { "search" => "전적 검색", "settings" => "설정", "help" => "사용 가이드", _ => "현재 매칭" };
    public string PageDescription => CurrentPage switch { "search" => "궁금한 플레이어의 전적을 확인하세요", "settings" => "이름 확인과 조회 방식을 관리하세요", "help" => "팀 정보를 더 편하게 확인하는 방법", _ => "이번 팀의 이름과 전적을 한곳에서" };
    public string QueryCondition => !int.TryParse(SeasonInput, out var season) || season < 1 ? "조회 조건 미설정" : (Ranked ? "랭크 / 스쿼드" : "일반 / 스쿼드") + " / 시즌 " + season;
    public string ServiceMessage => string.IsNullOrWhiteSpace(ApiKey) ? "현재 전적 조회를 사용할 수 없어요. 이름 확인은 계속 사용할 수 있습니다." : "조회 조건: " + QueryCondition;
    public bool ShowSearchService => !SearchHasResult || !string.IsNullOrWhiteSpace(ApiKey);
    public bool CanTeamLookup => VisiblePlayers.Any(p => p.HasConfirmedName) && !IsLookupBusy;
    public bool CanSearch => !IsLookupBusy;
    public string LookupLabel => IsLookupBusy ? "불러오는 중…" : "전적 조회";
    public string VersionLabel => $"EternalGG / v{typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)}";
    private int queryVersion;
    public bool HelpOpen { get => CurrentPage == "help"; set { if (value) CurrentPage = "help"; else if (HelpOpen) CurrentPage = "match"; } }
    [ObservableProperty] private string settingsMessage = "";
    public string ApiStatus => string.IsNullOrWhiteSpace(ApiKey) ? "키 미설정" : "키 입력됨";
    public string SelfNickname { get => Settings.SelfNickname; set { Settings.SelfNickname = value.Trim(); OnPropertyChanged(); } }
    public string LogPath { get => Settings.LogPath; set { Settings.LogPath = value; OnPropertyChanged(); } }

    /** 저장 설정 복원 및 초기 화면 준비 */
    public MainViewModel()
    {
        SelfNickname = Settings.SelfNickname;
        SeasonInput = Settings.Season > 0 ? Settings.Season.ToString() : "";
        Ranked = Settings.Ranked;
        AutoDetect = Settings.AutoDetect;
        BringForward = Settings.BringForward;
        foreach (var player in Players)
            player.PropertyChanged += (_, args) => { if (args.PropertyName is nameof(PlayerCard.HasName) or nameof(PlayerCard.HasSlot) or nameof(PlayerCard.HasConfirmedName)) NotifyTeam(); };
        AddEvent("EternalGG 시작");
    }

    /** API 키 변경 안내 갱신 */
    partial void OnApiKeyChanged(string value)
    {
        OnPropertyChanged(nameof(ApiStatus));
        InvalidateQueries();
    }

    /** 화면 이동과 선택 상태 갱신 */
    [RelayCommand]
    private void Navigate(string page) => CurrentPage = page;

    /** 페이지별 탐색 상태 동기화 */
    partial void OnCurrentPageChanged(string value)
    {
        foreach (var name in new[] { nameof(MatchOpen), nameof(SearchOpen), nameof(SettingsOpen), nameof(HelpOpen), nameof(PageTitle), nameof(PageDescription) })
            OnPropertyChanged(name);
    }

    /** 조회 조건 변경 시 이전 결과 분리 */
    partial void OnSeasonInputChanged(string value) => InvalidateQueries();

    /** 조회 모드 변경 시 이전 결과 분리 */
    partial void OnRankedChanged(bool value) => InvalidateQueries();

    /** 개발 설정 비활성화 시 인증 정보 해제 */
    partial void OnDeveloperModeChanged(bool value)
    {
        if (!value) ApiKey = "";
    }

    /** 응답 적용 범위와 조회 조건 갱신 */
    private void InvalidateQueries()
    {
        queryVersion++;
        foreach (var card in Players.Append(SearchPlayer)) { card.ClearStats(); card.Status = "전적 조회 대기"; }
        SearchHasResult = false;
        LastUpdated = "아직 조회하지 않음";
        OnPropertyChanged(nameof(ServiceMessage));
        OnPropertyChanged(nameof(QueryCondition));
    }

    /** 조회 버튼 진행 상태 갱신 */
    partial void OnIsLookupBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(LookupLabel));
        OnPropertyChanged(nameof(CanTeamLookup));
        OnPropertyChanged(nameof(CanSearch));
    }

    /** 검색 입력 변경 시 이전 결과 분리 */
    partial void OnSearchNicknameChanged(string value)
    {
        SearchHasResult = false;
        SearchMessage = "Enter 키 또는 검색 버튼으로 조회하세요";
    }

    /** 팀 구성과 빈 화면 동기화 */
    private void NotifyTeam()
    {
        var slots = Players.Take(Match.IsAi ? 1 : 3).ToArray();
        var active = Match.Phase is MatchPhase.Matched or MatchPhase.Loading or MatchPhase.InGame;
        var roster = active || slots.Any(p => p.HasSlot) ? slots : [];
        foreach (var card in VisiblePlayers.Except(roster).ToArray()) VisiblePlayers.Remove(card);
        for (var i = 0; i < roster.Length; i++)
            if (!VisiblePlayers.Contains(roster[i])) VisiblePlayers.Insert(i, roster[i]);
        PlayerCount = $"{VisiblePlayers.Count(p => p.HasName)} / {(Match.IsAi ? 1 : 3)}";
        OnPropertyChanged(nameof(HasPlayers));
        OnPropertyChanged(nameof(EmptyTeam));
        OnPropertyChanged(nameof(CanTeamLookup));
    }

    /** 연결 설정 표시 전환 */
    [RelayCommand]
    private void ToggleSettings() => SettingsOpen = !SettingsOpen;

    /** 사용 가이드 표시 전환 */
    [RelayCommand]
    private void ToggleHelp() => HelpOpen = !HelpOpen;

    /** API 키를 제외한 사용자 설정 저장 */
    [RelayCommand]
    private void SaveSettings()
    {
        Settings.Season = int.TryParse(SeasonInput, out var season) ? season : 0;
        Settings.Ranked = Ranked;
        Settings.AutoDetect = AutoDetect;
        Settings.BringForward = BringForward;
        try
        {
            Settings.Save();
            logs.Dispose();
            initial = true;
            SettingsMessage = "설정을 저장했어요";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SettingsMessage = "설정을 저장하지 못했어요. 다시 시도해 주세요.";
        }
        AddEvent("설정 저장 / 자동 감지 " + (AutoDetect ? "켜짐" : "꺼짐"));
    }

    /** 수동 이름 재인식 구간 시작 */
    [RelayCommand]
    private void Recognize()
    {
        if (GameCapture.FindGame() == 0) { NameMessage = "이터널리턴을 실행한 뒤 이름을 다시 확인해 주세요"; return; }
        manualCaptureUntil = DateTime.Now.AddSeconds(40);
        ResetAutomaticNames();
        NameMessage = OcrStatus = "게임을 앞으로 가져오세요. 40초 동안 이름을 다시 확인합니다.";
        AddEvent("40초 동안 이름 확인 / 게임을 전경으로 전환해 주세요");
    }

    /** 현재 팀의 전적 조회 */
    [RelayCommand]
    private async Task LookupAsync() => await QueryAsync(VisiblePlayers.Where(p => p.HasConfirmedName).ToArray(), true);

    /** 현재 팀과 분리된 닉네임 검색 */
    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchNickname)) { SearchMessage = "검색할 닉네임을 입력해 주세요"; return; }
        SearchPlayer.Nickname = SearchNickname.Trim();
        SearchHasResult = true;
        SearchMessage = "";
        await QueryAsync([SearchPlayer], false);
    }

    /** 조회 중복 방지와 응답 유효 범위 확인 */
    private async Task QueryAsync(PlayerCard[] cards, bool team)
    {
        if (IsLookupBusy) return;
        if (cards.Length == 0) { NameMessage = "이름이 확인되면 전적을 조회할 수 있어요"; return; }
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            foreach (var card in cards) card.Apply(new(null, team ? "전적 미조회" : "현재 전적 조회를 사용할 수 없어요"));
            return;
        }

        if (!int.TryParse(SeasonInput, out var season) || season < 1)
        {
            foreach (var card in cards) card.Apply(new(null, "조회 조건이 준비되지 않았어요"));
            return;
        }

        IsLookupBusy = true;
        var generation = Match.Generation;
        var version = queryVersion;
        var key = ApiKey;
        var mode = Ranked ? 3 : 2;
        var requests = cards.Select(card => (Card: card, Name: card.Nickname, Revision: card.Revision)).ToArray();
        try
        {
            var api = new EternalReturnApi(http);
            foreach (var item in requests)
            {
                if (disposed || version != queryVersion || team && generation != Match.Generation) break;
                if (item.Revision != item.Card.Revision) continue;
                item.Card.Status = "전적 불러오는 중…";
                if (item.Card.HasStats) item.Card.FetchedAt = "이전 조회 결과 / " + item.Card.FetchedAt.Replace("이전 조회 결과 / ", "");
                var result = await api.LookupAsync(item.Name, key, season, mode, lifetime.Token);
                if (disposed || version != queryVersion || team && generation != Match.Generation || item.Revision != item.Card.Revision) continue;
                item.Card.Apply(result);
                if (result.Stats != null) LastUpdated = DateTime.Now.ToString("HH:mm");
            }
        }
        finally { IsLookupBusy = false; }
    }

    /** 상태 변경별 한 번의 창 표시 요청 */
    public void ObserveGameAttention(bool running, MatchPhase phase, int generation, bool namesReady, DateTimeOffset now)
    {
        var notice = attention.Observe(running, phase, generation, namesReady, now);
        if (notice == GameNotice.None) return;
        LatestNotice = now.ToLocalTime().ToString("HH:mm") + (notice == GameNotice.Connected ? " / 게임 연결 확인" : " / 새 매칭 확인");
        if (!BringForward) return;
        if (notice == GameNotice.MatchDetected && !namesReady && NameMessage.Length == 0)
            NameMessage = "게임으로 돌아가면 팀원 이름 확인을 계속합니다";
        CurrentPage = "match";
        AttentionRequested?.Invoke(this, EventArgs.Empty);
    }

    /** 현재 팀 이름의 확정 상태로 알림 시점 갱신 */
    private void UpdateAttention(bool running)
    {
        var ready = Players.Take(Match.IsAi ? 1 : 3).All(p => p.HasConfirmedName);
        if (ready && NameMessage == "게임으로 돌아가면 팀원 이름 확인을 계속합니다") NameMessage = "";
        ObserveGameAttention(running, Match.Phase, Match.Generation, ready, DateTimeOffset.Now);
    }

    /** 누락을 포함한 한 프레임의 이름 확정과 수동 수정 보존 */
    public bool ApplyNameFrame(IReadOnlyList<OcrCandidate> candidates)
    {
        var expected = Match.IsAi ? 1 : 3;

        /** 낮은 신뢰도도 이름 입력란에 표시하고 자동 확정과 분리 */
        foreach (var group in candidates.Where(c => c.Slot >= 0 && c.Slot < expected && float.IsFinite(c.Confidence) && NicknameRules.Normalize(c.Name) == c.Name).GroupBy(c => c.Slot))
        {
            var suggestion = group.OrderBy(c => c.RequiresReview).ThenByDescending(c => c.Confidence).First();
            if (suggestion.Slot == 0 && !string.IsNullOrWhiteSpace(SelfNickname) && suggestion.Name != SelfNickname) continue;
            Players[suggestion.Slot].SetSuggestedName(suggestion.Name, suggestion.RequiresReview || suggestion.Confidence < NicknameConsensus.MinimumConfidence, suggestion.Confidence);
        }

        var confirmed = names.ObserveFrame(candidates.Where(c => !c.RequiresReview).Select(c => (c.Slot, c.Name, c.Confidence)), expected);
        var added = false;
        foreach (var candidate in confirmed)
        {
            if (candidate.Slot == 0 && !string.IsNullOrWhiteSpace(SelfNickname) && candidate.Name != SelfNickname)
            {
                NameMessage = "읽은 이름이 등록한 본인 이름과 달라요. 닉네임을 직접 확인해 주세요.";
                continue;
            }

            var confidence = candidates.Where(c => c.Slot == candidate.Slot && c.Name == candidate.Name && !c.RequiresReview).Max(c => c.Confidence);
            if (!Players[candidate.Slot].SetDetectedName(candidate.Name, confidence)) continue;
            AddEvent($"플레이어 {candidate.Slot + 1} 이름 확인 완료");
            added = true;
        }

        if (Players.Take(expected).All(p => p.HasConfirmedName)) NameMessage = "";
        NotifyTeam();
        return added;
    }

    /** 직접 입력을 보존하는 자동 이름 재확인 준비 */
    public void ResetAutomaticNames()
    {
        names.Reset();
        foreach (var player in Players.Where(p => !p.UserEdited)) player.Reset();
        if (!Players[0].UserEdited && !string.IsNullOrWhiteSpace(SelfNickname)) Players[0].SetRegisteredName(SelfNickname);
        if (Players.All(p => !p.HasStats)) LastUpdated = "아직 조회하지 않음";
        OcrDiagnostic = "다시 확인 대기";
        NotifyTeam();
    }

    /** 로그 한 줄의 새 매칭과 이름 확인 구간 반영 */
    public void ApplyGameLog(string line)
    {
        var oldGeneration = Match.Generation;
        if (!Match.Apply(line)) return;
        ModeLabel = Match.Mode;
        AddEvent("상태 변경 / " + Match.Phase + " / " + Match.Mode);
        if (oldGeneration != Match.Generation) ClearTeam();
        if (Match.Phase is MatchPhase.Matched or MatchPhase.Loading) StartNameCapture();
        if (Match.Phase is MatchPhase.Lobby or MatchPhase.Finished) captureUntil = DateTime.MinValue;
        if (Match.Phase == MatchPhase.InGame)
        {
            captureUntil = DateTime.MinValue;
            if (NameMessage.Length == 0 && !Players.Take(Match.IsAi ? 1 : 3).All(p => p.HasConfirmedName))
                NameMessage = "확인되지 않은 팀원은 닉네임을 입력하거나 이름 다시 확인을 눌러 주세요";
        }

        if (Match.Phase == MatchPhase.Lobby) ClearTeam();
        NotifyTeam();
    }

    /** 연결 시 현재 단계 복원과 선택 화면의 새 이름 확인 */
    public void RestoreGameHistory(IEnumerable<string> lines)
    {
        foreach (var line in lines) Match.Apply(line);
        ResetAutomaticNames();
        captureUntil = manualCaptureUntil = DateTime.MinValue;
        if (Match.Phase is MatchPhase.Matched or MatchPhase.Loading) StartNameCapture();
        NotifyTeam();
    }

    /** 선택 화면의 인식 기간과 미확정 본인 이름 준비 */
    private void StartNameCapture()
    {
        captureUntil = DateTime.Now.AddSeconds(120);
        if (!Players[0].HasName && !Players[0].UserEdited && !string.IsNullOrWhiteSpace(SelfNickname))
            Players[0].SetRegisteredName(SelfNickname);
    }

    /** 게임 연결과 로그 및 인식 상태의 주기 갱신 */
    public async Task TickAsync()
    {
        if (ticking || disposed) return;
        ticking = true;
        try
        {
            var running = GameCapture.FindGame() != 0;
            GameStatus = running ? "게임 연결됨" : "게임 실행 대기";

            /** 게임 종료 시 팀 정보와 감지 상태 정리 */
            if (!running)
            {
                if (wasRunning) { Match.Reset(); ClearTeam(); captureUntil = DateTime.MinValue; manualCaptureUntil = DateTime.MinValue; logs.Dispose(); initial = true; }
                wasRunning = false;
                UpdateAttention(false);
                LatestNotice = "";
                PhaseTitle = "이터널리턴을 실행해 주세요";
                PhaseDetail = AutoDetect ? "게임 실행 후 팀원 이름을 자동으로 확인합니다. 직접 검색도 사용할 수 있어요." : "닉네임으로 검색하거나 게임 실행 후 이름 다시 확인을 눌러 주세요.";
                return;
            }

            wasRunning = true;

            /** 최초 연결 시 과거 로그의 상태만 복원 */
            if (initial && File.Exists(LogPath))
            {
                using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                RestoreGameHistory((await reader.ReadToEndAsync()).Split('\n'));

                logs.Open(LogPath, true);
                initial = false;
                AddEvent("게임 로그 연결 / 이름은 현재 화면에서 다시 확인");
            }

            /** 새 로그의 매칭 전환과 이름 확인 구간 반영 */
            LogStatus = File.Exists(LogPath) ? "연결됨" : "경로 확인 필요";
            foreach (var line in logs.ReadNew(LogPath)) ApplyGameLog(line);

            /** 자동 또는 수동 인식 구간의 캡처 간격 제한 */
            RefreshPhase();
            NotifyTeam();
            UpdateAttention(true);
            var manual = DateTime.Now < manualCaptureUntil;
            if ((AutoDetect && DateTime.Now < captureUntil || manual) && (DateTime.Now - lastCapture).TotalSeconds >= 1.2)
            {
                lastCapture = DateTime.Now;
                var generation = Match.Generation;

                /** 백그라운드 로컬 인식 및 AI 아군 봇 제외 */
                var candidates = await Task.Run(() =>
                {
                    using var screenshot = GameCapture.CaptureForeground();
                    if (screenshot == null) return null;
                    var regions = Match.Phase == MatchPhase.InGame ? Settings.GameRegions : Settings.SelectionRegions;
                    if (Match.IsAi) regions = regions.Take(1).ToList();
                    return ocr.Read(screenshot, regions, Match.Phase != MatchPhase.InGame);
                });
                if (disposed || generation != Match.Generation) return;
                if (candidates == null)
                {
                    names.Reset();
                    OcrStatus = "게임 화면이 전경일 때 인식";
                }
                else
                {
                    OcrDiagnostic = ocr.Diagnostics;
                    OcrStatus = candidates.Count == 0 ? "이름 표시 영역 확인 중" : "이름 후보 확인 중";
                    var added = ApplyNameFrame(candidates);

                    /** 이름 확정 후 전적 조회 및 인식 완료 처리 */
                    UpdateAttention(true);
                    if (added && !string.IsNullOrWhiteSpace(ApiKey)) await QueryAsync(VisiblePlayers.Where(p => p.HasConfirmedName).ToArray(), true);
                    var expected = Match.IsAi ? 1 : 3;
                    if (Players.Take(expected).All(p => p.HasConfirmedName))
                    {
                        OcrStatus = "이름 확인 완료";
                        captureUntil = manualCaptureUntil = DateTime.MinValue;
                    }
                }
            }

            if (DateTime.Now >= manualCaptureUntil && NameMessage.Contains("40초")) NameMessage = "이름 확인이 끝났어요. 잘못된 이름은 카드에서 수정할 수 있습니다.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TesseractException or System.ComponentModel.Win32Exception or Microsoft.ML.OnnxRuntime.OnnxRuntimeException or DllNotFoundException or BadImageFormatException || e is TypeInitializationException { InnerException: DllNotFoundException })
        {
            names.Reset();
            NameMessage = "이름을 확인하지 못했어요. 팀원 카드에 닉네임을 직접 입력할 수 있습니다.";
            OcrStatus = "인식 환경 확인 필요";
            if (Events.FirstOrDefault()?.Contains("환경 오류") != true) AddEvent("환경 오류 / " + e.GetType().Name);
        }
        finally
        {
            ticking = false;
            if (disposed) ocr.Dispose();
        }
    }

    /** 매칭 단계별 화면 문구 갱신 */
    private void RefreshPhase()
    {
        ModeLabel = Match.Mode;
        (PhaseTitle, PhaseDetail) = Match.Phase switch
        {
            MatchPhase.Queuing => ("매칭을 찾고 있어요", "매칭이 잡히면 자동으로 팀원 이름을 확인합니다."),
            MatchPhase.Matched => ("매칭이 잡혔어요", "캐릭터 선택 화면에서 아군 이름을 확인하고 있습니다."),
            MatchPhase.Loading => ("게임에 입장하고 있어요", "이름이 표시되면 자동으로 전적 조회를 준비합니다."),
            MatchPhase.InGame => ("지금, 루미아 섬에서", "확인한 팀원 정보가 유지됩니다. 게임을 방해하지 않아요."),
            MatchPhase.Finished => ("경기가 끝났어요", "다음 매칭을 기다립니다. AI 경기는 공식 전적이 없을 수 있어요."),
            _ => ("다음 매칭을 기다리는 중", "게임이 시작되면 팀원 이름을 자동으로 확인합니다.")
        };
        if (!AutoDetect && Match.Phase is MatchPhase.Lobby or MatchPhase.Queuing or MatchPhase.Matched or MatchPhase.Loading)
            PhaseDetail = "자동 이름 확인이 꺼져 있어요. 이름 다시 확인을 누르거나 닉네임을 검색해 주세요.";
    }

    /** 다음 매칭을 위한 팀 정보 초기화 */
    private void ClearTeam()
    {
        LatestNotice = "";
        captureUntil = manualCaptureUntil = DateTime.MinValue;
        OcrStatus = "대기";
        foreach (var card in Players) card.Reset();
        NameMessage = "";
        LastUpdated = "아직 조회하지 않음";
        names.Reset();
        NotifyTeam();
    }

    /** 최근 상태 이벤트의 표시 개수 제한 */
    private void AddEvent(string value)
    {
        Events.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + value);
        while (Events.Count > 8) Events.RemoveAt(Events.Count - 1);
    }

    /** 비동기 작업 취소 및 연결 자원 해제 */
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        logs.Dispose();
        http.Dispose();
        lifetime.Dispose();

        /** 진행 중인 인식 종료 후 네이티브 자원 해제 */
        if (!ticking) ocr.Dispose();
    }
}
