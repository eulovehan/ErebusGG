using CommunityToolkit.Mvvm.ComponentModel;
using EternalGG.Core;

namespace EternalGG.App.ViewModels;

/** 플레이어별 이름과 전적 표시 상태 */
public partial class PlayerCard : ObservableObject
{
    [ObservableProperty] private string nickname = "";
    [ObservableProperty] private string source = "이름 확인 대기";
    [ObservableProperty] private string status = "자동 확인되지 않으면 닉네임을 직접 입력해 주세요";
    [ObservableProperty] private string mmr = "—";
    [ObservableProperty] private string games = "—";
    [ObservableProperty] private string winRate = "—";
    [ObservableProperty] private string averageRank = "—";
    [ObservableProperty] private string recent = "";
    [ObservableProperty] private bool hasStats;
    [ObservableProperty] private bool hasSlot;
    [ObservableProperty] private bool screenVerified;
    [ObservableProperty] private string fetchedAt = "";
    public string Label { get; init; } = "ALLY";
    public bool HasName => !string.IsNullOrWhiteSpace(Nickname);
    public bool IsReadOnly { get; init; }
    public int Revision { get; private set; }
    public bool UserEdited { get; private set; }
    public bool HasConfirmedName => HasName && (ScreenVerified || UserEdited);
    private bool detecting;

    /** 닉네임 변경에 따른 기존 전적 초기화 */
    partial void OnNicknameChanged(string value)
    {
        ClearStats();
        ScreenVerified = false;
        Revision++;
        if (!string.IsNullOrWhiteSpace(value)) HasSlot = true;
        if (!detecting) UserEdited = HasSlot;
        Source = detecting ? "자동 확인" : "직접 입력";
        Status = string.IsNullOrWhiteSpace(value) ? "이름 확인 대기" : "전적 조회 대기";
        OnPropertyChanged(nameof(HasName));
    }

    /** 직접 수정한 이름을 보존하는 자동 이름 반영 */
    public bool SetDetectedName(string value)
    {
        if (UserEdited || Nickname == value && ScreenVerified) return false;
        detecting = true;
        try { Nickname = value; }
        finally { detecting = false; }
        Source = "자동 확인";
        ScreenVerified = true;
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
        Source = "등록한 이름 / 확인 필요";
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
        HasSlot = false;
        Revision++;
        ClearStats();
    }

    /** 전적 수치와 최근 경기 표시 초기화 */
    public void ClearStats()
    {
        Mmr = Games = WinRate = AverageRank = "—";
        Recent = FetchedAt = "";
        HasStats = false;
    }

    /** 공식 조회 결과의 카드 표시 반영 */
    public void Apply(LookupResult result)
    {
        Status = result.Message;
        if (result.Stats is not { } stats)
        {
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
    }
}
