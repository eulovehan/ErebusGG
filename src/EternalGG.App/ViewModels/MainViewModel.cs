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

public partial class MainViewModel : ObservableObject, IDisposable
{
    public AppSettings Settings { get; } = AppSettings.Load();
    public ObservableCollection<PlayerCard> Players { get; } = [new() { Label = "01 / YOU" }, new() { Label = "02 / ALLY" }, new() { Label = "03 / ALLY" }];
    public ObservableCollection<string> Events { get; } = [];
    public MatchState Match { get; } = new();
    private readonly LogMonitor logs = new();
    private readonly OcrService ocr = new();
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<int, (string Name, int Hits)> votes = [];
    private DateTime captureUntil;
    private DateTime lastCapture;
    private DateTime manualCaptureUntil;
    private bool ticking;
    private bool disposed;
    private bool wasRunning;
    private bool initial = true;

    [ObservableProperty] private string gameStatus = "게임 연결 확인 중";
    [ObservableProperty] private string phaseTitle = "다음 매칭을 기다리는 중";
    [ObservableProperty] private string phaseDetail = "게임과 연결되면 팀원 이름을 자동으로 확인합니다.";
    [ObservableProperty] private string modeLabel = "자동 감지";
    [ObservableProperty] private string logStatus = "확인 중";
    [ObservableProperty] private string ocrStatus = "대기";
    [ObservableProperty] private string ocrDiagnostic = "아직 읽지 않음";
    [ObservableProperty] private string lastUpdated = "아직 조회하지 않음";
    [ObservableProperty] private string playerCount = "0 / 3";
    [ObservableProperty] private string apiKey = "";
    [ObservableProperty] private string seasonInput = "";
    [ObservableProperty] private bool ranked;
    [ObservableProperty] private bool autoDetect = true;
    [ObservableProperty] private bool settingsOpen;
    [ObservableProperty] private bool helpOpen;
    [ObservableProperty] private string settingsMessage = "키는 현재 실행 중에만 보관하며 파일에 저장하지 않습니다.";
    public string ApiStatus => string.IsNullOrWhiteSpace(ApiKey) ? "키 미설정" : "키 입력됨";
    public string ApiBanner => string.IsNullOrWhiteSpace(ApiKey) ? "이름 감지는 사용할 수 있어요. 전적은 API 키를 입력한 후 표시됩니다." : "공식 API 연결 준비 완료 · 조회를 눌러 전적을 확인하세요.";
    public string SelfNickname { get => Settings.SelfNickname; set { Settings.SelfNickname = value.Trim(); OnPropertyChanged(); } }
    public string LogPath { get => Settings.LogPath; set { Settings.LogPath = value; OnPropertyChanged(); } }

    public MainViewModel()
    {
        SelfNickname = Settings.SelfNickname;
        SeasonInput = Settings.Season > 0 ? Settings.Season.ToString() : "";
        Ranked = Settings.Ranked;
        AutoDetect = Settings.AutoDetect;
        AddEvent("EternalGG 시작 · 로컬 인식 / 실제 전적만 표시");
    }

    partial void OnApiKeyChanged(string value)
    {
        OnPropertyChanged(nameof(ApiStatus));
        OnPropertyChanged(nameof(ApiBanner));
    }

    [RelayCommand]
    private void ToggleSettings() => SettingsOpen = !SettingsOpen;

    [RelayCommand]
    private void ToggleHelp() => HelpOpen = !HelpOpen;

    [RelayCommand]
    private void SaveSettings()
    {
        Settings.Season = int.TryParse(SeasonInput, out var season) ? season : 0;
        Settings.Ranked = Ranked;
        Settings.AutoDetect = AutoDetect;
        Settings.Save();
        SettingsMessage = "설정 저장 완료 · API 키는 디스크에 저장하지 않았습니다.";
        AddEvent("설정 저장 · 자동 감지 " + (AutoDetect ? "켜짐" : "꺼짐"));
    }

    [RelayCommand]
    private void Recognize()
    {
        manualCaptureUntil = DateTime.Now.AddSeconds(40);
        votes.Clear();
        OcrStatus = "게임 화면을 앞으로 가져오세요";
        AddEvent("40초 동안 이름 확인 · 게임을 전경으로 전환해 주세요");
    }

    [RelayCommand]
    private async Task LookupAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            foreach (var card in Players.Where(p => p.HasName)) { card.ClearStats(); card.Status = "API 키 미설정 · 전적 조회 대기"; }
            SettingsOpen = true;
            SettingsMessage = "실제 전적 조회에는 API 키가 필요합니다. 이름 감지 테스트는 키 없이 가능합니다.";
            return;
        }

        if (!int.TryParse(SeasonInput, out var season) || season < 1)
        {
            SettingsOpen = true;
            SettingsMessage = "조회할 시즌 ID를 입력해 주세요. 임의 시즌을 조회하지 않습니다.";
            return;
        }

        var generation = Match.Generation;
        var api = new EternalReturnApi(http);
        foreach (var card in Players.Where(p => p.HasName))
        {
            var nickname = card.Nickname;
            card.ClearStats();
            card.Status = "공식 API 조회 중…";
            var result = await api.LookupAsync(nickname, ApiKey, season, Ranked ? 3 : 2, lifetime.Token);
            if (generation == Match.Generation && nickname == card.Nickname) card.Apply(result);
        }

        LastUpdated = DateTime.Now.ToString("HH:mm:ss");
        AddEvent("전적 조회 처리 완료 · " + LastUpdated);
    }

    public async Task TickAsync()
    {
        if (ticking || disposed) return;
        ticking = true;
        try
        {
            var running = GameCapture.FindGame() != 0;
            GameStatus = running ? "ETERNAL RETURN 연결됨" : "게임 실행 대기";
            if (!running)
            {
                if (wasRunning) { ClearTeam(); Match.Reset(); captureUntil = DateTime.MinValue; manualCaptureUntil = DateTime.MinValue; logs.Dispose(); initial = true; }
                wasRunning = false;
                PhaseTitle = "이터널리턴을 실행해 주세요";
                PhaseDetail = "자동 감지는 게임 실행 후 시작됩니다. 직접 검색도 사용할 수 있어요.";
                return;
            }

            wasRunning = true;
            if (initial && File.Exists(LogPath))
            {
                using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                foreach (var line in (await reader.ReadToEndAsync()).Split('\n'))
                {
                    Match.Apply(line);
                }

                logs.Open(LogPath, true);
                initial = false;
                AddEvent("게임 로그 연결 · 과거 매칭 정보는 표시하지 않음");
            }

            LogStatus = File.Exists(LogPath) ? "연결됨" : "경로 확인 필요";
            foreach (var line in logs.ReadNew(LogPath))
            {
                var oldGeneration = Match.Generation;
                if (!Match.Apply(line)) continue;
                ModeLabel = Match.Mode;
                AddEvent("상태 변경 · " + Match.Phase + " · " + Match.Mode);
                if (oldGeneration != Match.Generation) ClearTeam();
                if (Match.Phase is MatchPhase.Matched or MatchPhase.Loading)
                {
                    captureUntil = DateTime.Now.AddSeconds(120);
                    if (Players.All(p => !p.HasName) && !string.IsNullOrWhiteSpace(SelfNickname))
                    {
                        Players[0].Nickname = SelfNickname;
                        Players[0].Source = "등록한 본인 · 화면 확인 대기";
                    }
                }

                if (Match.Phase is MatchPhase.Lobby or MatchPhase.Finished) captureUntil = DateTime.MinValue;
                if (Match.Phase == MatchPhase.InGame) captureUntil = DateTime.MinValue;
                if (Match.Phase == MatchPhase.Lobby) ClearTeam();
            }

            RefreshPhase();
            var manual = DateTime.Now < manualCaptureUntil;
            if ((AutoDetect && DateTime.Now < captureUntil || manual) && (DateTime.Now - lastCapture).TotalSeconds >= 1.2)
            {
                lastCapture = DateTime.Now;
                var generation = Match.Generation;
                var candidates = await Task.Run(() =>
                {
                    using var screenshot = GameCapture.CaptureForeground();
                    if (screenshot == null) return null;
                    var regions = Match.Phase == MatchPhase.InGame ? Settings.GameRegions : Settings.SelectionRegions;
                    if (Match.IsAi) regions = regions.Take(1).ToList();
                    return ocr.Read(screenshot, regions);
                });
                if (generation != Match.Generation) return;
                if (candidates == null) OcrStatus = "게임 화면이 전경일 때 인식";
                else
                {
                    OcrDiagnostic = ocr.Diagnostics;
                    OcrStatus = candidates.Count == 0 ? "이름 표시 영역 확인 중" : "이름 후보 확인 중";
                    var added = false;
                    foreach (var candidate in candidates)
                    {
                        var previous = votes.GetValueOrDefault(candidate.Slot);
                        var hits = previous.Name == candidate.Name ? previous.Hits + 1 : 1;
                        votes[candidate.Slot] = (candidate.Name, hits);
                        if (hits < 2) continue;
                        var card = Players[candidate.Slot];
                        if (card.Nickname != candidate.Name || card.Source.Contains("대기"))
                        {
                            card.Nickname = candidate.Name;
                            card.Source = "화면 OCR · 2회 일치";
                            AddEvent($"플레이어 {candidate.Slot + 1} 이름 인식 완료");
                            added = true;
                        }
                    }

                    if (added && !string.IsNullOrWhiteSpace(ApiKey)) await LookupAsync();
                    var expected = Match.IsAi ? 1 : 3;
                    if (Players.Take(expected).All(p => p.Source.StartsWith("화면 OCR")))
                    {
                        OcrStatus = "이름 확인 완료";
                        captureUntil = manualCaptureUntil = DateTime.MinValue;
                    }
                }
            }

            PlayerCount = $"{Players.Count(p => p.HasName)} / {(Match.IsAi ? 1 : 3)}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TesseractException or System.ComponentModel.Win32Exception)
        {
            OcrStatus = "인식 환경 확인 필요";
            if (Events.FirstOrDefault()?.Contains("환경 오류") != true) AddEvent("환경 오류 · " + e.GetType().Name);
        }
        finally { ticking = false; }
    }

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
    }

    private void ClearTeam()
    {
        captureUntil = manualCaptureUntil = DateTime.MinValue;
        OcrStatus = "대기";
        foreach (var card in Players) { card.Nickname = ""; card.Source = "이름 확인 대기"; card.ClearStats(); }
        votes.Clear();
        PlayerCount = "0 / 3";
    }

    private void AddEvent(string value)
    {
        Events.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + value);
        while (Events.Count > 8) Events.RemoveAt(Events.Count - 1);
    }

    public void Dispose()
    {
        disposed = true;
        lifetime.Cancel();
        logs.Dispose();
        http.Dispose();
        lifetime.Dispose();
    }
}
