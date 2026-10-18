using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EternalGG.Core;

namespace EternalGG.App.ViewModels;

/** 플레이어별 이름과 전적 표시 상태 */
public partial class PlayerCard : ObservableObject
{
    [ObservableProperty] private string nickname = "";
    [ObservableProperty] private string source = "이름 확인 대기";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayStatus))] private string status = "자동 확인되지 않으면 닉네임을 직접 입력해 주세요";
    [ObservableProperty] private string mmr = "—";
    [ObservableProperty] private string games = "—";
    [ObservableProperty] private string winRate = "—";
    [ObservableProperty] private string averageRank = "—";
    [ObservableProperty] private string recent = "";
    [ObservableProperty] private string seasonScope = "";
    [ObservableProperty] private bool hasStats;

    /** 현재와 직전 정규 시즌의 독립 티어 표시 */
    [ObservableProperty] private RankTier? currentTier;
    [ObservableProperty] private RankTier? previousTier;

    /** 최근 랭크 표본의 화면 상태 */
    [ObservableProperty] private double rankedPercent;
    [ObservableProperty] private string rankedWinRate = "—";
    [ObservableProperty] private string rankedSummary = "전적 조회 대기";
    [ObservableProperty] private string picksSummary = "전적을 조회하면 표시됩니다";
    public ObservableCollection<MostPick> MostPicks { get; } = [];

    /** 이름 확인과 카드 식별 상태 */
    [ObservableProperty] private bool hasSlot;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasConfirmedName))] private bool screenVerified;
    [ObservableProperty] private bool needsNameConfirmation;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ConfidenceGrade)), NotifyPropertyChangedFor(nameof(ConfidenceLabel)), NotifyPropertyChangedFor(nameof(HasRecognitionConfidence))] private float? recognitionConfidence;
    [ObservableProperty] private string fetchedAt = "";

    /** 중복 대기 안내를 제외한 작업 결과 */
    public string DisplayStatus => Status is "전적 조회 대기" or "전적 조회 완료" ? "" : Status;
    public string Label { get; init; } = "ALLY";
    public bool HasName => !string.IsNullOrWhiteSpace(Nickname);
    public bool IsReadOnly { get; init; }
    public int Revision { get; private set; }
    public bool UserEdited { get; private set; }
    public bool HasConfirmedName => HasName && (ScreenVerified || UserEdited);

    /** 인식 점수의 사용자 등급 표시 */
    public bool HasRecognitionConfidence => RecognitionConfidence.HasValue;
    public string ConfidenceGrade => RecognitionConfidence switch { null => "", < 0.4f => "낮음", < 0.7f => "보통", _ => "높음" };
    public string ConfidenceLabel => "인식 신뢰도 / " + ConfidenceGrade;
    private bool detecting;

    /** 닉네임 변경에 따른 기존 전적 초기화 */
    partial void OnNicknameChanged(string value)
    {
        ClearStats();
        ScreenVerified = false;
        NeedsNameConfirmation = false;
        RecognitionConfidence = null;
        Revision++;
        if (!string.IsNullOrWhiteSpace(value)) HasSlot = true;
        if (!detecting) UserEdited = HasSlot;
        Source = detecting ? "자동 확인" : "직접 입력";
        Status = string.IsNullOrWhiteSpace(value) ? "이름 확인 대기" : "전적 조회 대기";
        OnPropertyChanged(nameof(HasName));
        OnPropertyChanged(nameof(HasConfirmedName));
    }

    /** 확정되지 않은 인식 이름의 입력란 표시 */
    public void SetSuggestedName(string value, bool uncertain, float confidence)
    {
        if (UserEdited || ScreenVerified) return;
        detecting = true;
        try { Nickname = value; }
        finally { detecting = false; }
        NeedsNameConfirmation = true;
        RecognitionConfidence = Math.Clamp(confidence, 0, 1);
        Source = uncertain ? "자동 입력" : "이름 확인 중";
        Status = "이름이 맞는지 확인하거나 직접 수정해 주세요";
    }

    /** 표시된 이름에 대한 사용자 확인 */
    [RelayCommand]
    private void ConfirmName()
    {
        if (!HasName || !NeedsNameConfirmation) return;
        UserEdited = true;
        NeedsNameConfirmation = false;
        Source = "직접 확인";
        Status = "전적 조회 대기";
        OnPropertyChanged(nameof(HasConfirmedName));
    }

    /** 직접 수정한 이름을 보존하는 자동 이름 반영 */
    public bool SetDetectedName(string value, float? confidence = null)
    {
        if (UserEdited || Nickname == value && ScreenVerified) return false;
        detecting = true;
        try { Nickname = value; }
        finally { detecting = false; }
        Source = "자동 확인";
        ScreenVerified = true;
        NeedsNameConfirmation = false;
        RecognitionConfidence = confidence;
        if (!HasStats) Status = "전적 조회 대기";
        return true;
    }

    /** 화면 확인 전 등록 이름의 별도 표시 */
    public void SetRegisteredName(string value)
    {
        if (UserEdited || ScreenVerified) return;
        detecting = true;
        try { Nickname = value; }
        finally { detecting = false; }
        ScreenVerified = false;
        NeedsNameConfirmation = true;
        Source = "등록한 이름";
        Status = "화면에서 본인 이름을 확인하고 있어요";
    }

    /** 다음 매칭의 입력 상태 초기화 */
    public void Reset()
    {
        detecting = true;
        try { Nickname = ""; }
        finally { detecting = false; }
        UserEdited = false;
        ScreenVerified = false;
        NeedsNameConfirmation = false;
        RecognitionConfidence = null;
        HasSlot = false;
        Revision++;
        ClearStats();
    }

    /** 전적 수치와 최근 경기 표시 초기화 */
    public void ClearStats()
    {
        Mmr = Games = WinRate = AverageRank = "—";
        Recent = FetchedAt = SeasonScope = "";
        HasStats = false;
        CurrentTier = PreviousTier = null;
        RankedPercent = 0;
        RankedWinRate = "—";
        RankedSummary = "전적 조회 대기";
        PicksSummary = "전적을 조회하면 표시됩니다";
        MostPicks.Clear();
    }

    /** 공식 조회 결과의 카드 표시 반영 */
    public void Apply(LookupResult result)
    {
        Status = result.Message;
        if (result.Stats is not { } stats)
        {
            if (!HasStats) { RankedSummary = "기록 미제공"; PicksSummary = "표시할 기록이 없습니다"; }
            if (HasStats) FetchedAt = "이전 조회 결과 / " + FetchedAt.Replace("이전 조회 결과 / ", "");
            return;
        }

        HasStats = true;
        FetchedAt = stats.FetchedAt.ToLocalTime().ToString("HH:mm") + " 조회";
        Mmr = stats.Mmr.ToString("N0");
        Games = stats.Games.ToString("N0");
        WinRate = stats.Games == 0 ? "—" : $"{100.0 * stats.Wins / stats.Games:0.0}%";
        AverageRank = stats.AverageRank.ToString("0.0") + "위";
        Recent = stats.Recent;
        SeasonScope = stats.SeasonScope;
        CurrentTier = stats.CurrentTier;
        PreviousTier = stats.PreviousTier;

        /** 시즌 통계와 최근 랭크 표본의 분리 */
        RankedPercent = stats.RecentRanked is { Games: > 0 } ranked ? 100.0 * ranked.Wins / ranked.Games : 0;
        RankedWinRate = stats.RecentRanked?.WinRate ?? "—";
        RankedSummary = stats.RecentRanked?.Summary ?? "최근 랭크 기록 미제공";
        MostPicks.Clear();
        foreach (var pick in stats.RecentRanked?.MostPicks ?? []) MostPicks.Add(pick);
        PicksSummary = MostPicks.Count > 0 ? "" : stats.RecentRanked is { Games: 0 } ? "플레이한 캐릭터가 없습니다" : "캐릭터 기록 미제공";
    }
}
