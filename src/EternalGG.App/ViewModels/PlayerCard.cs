using CommunityToolkit.Mvvm.ComponentModel;
using EternalGG.Core;

namespace EternalGG.App.ViewModels;

public partial class PlayerCard : ObservableObject
{
    [ObservableProperty] private string nickname = "";
    [ObservableProperty] private string source = "이름 확인 대기";
    [ObservableProperty] private string status = "아직 확인된 플레이어가 없습니다";
    [ObservableProperty] private string mmr = "—";
    [ObservableProperty] private string games = "—";
    [ObservableProperty] private string winRate = "—";
    [ObservableProperty] private string averageRank = "—";
    [ObservableProperty] private string recent = "공식 API 키를 입력하면 전적을 조회합니다";
    public string Label { get; init; } = "ALLY";
    public bool HasName => !string.IsNullOrWhiteSpace(Nickname);

    partial void OnNicknameChanged(string value)
    {
        ClearStats();
        Source = string.IsNullOrWhiteSpace(value) ? "이름 확인 대기" : "직접 입력 · 이름을 확인해 주세요";
        Status = string.IsNullOrWhiteSpace(value) ? "아직 확인된 플레이어가 없습니다" : "API 키 미설정 · 전적 조회 대기";
        OnPropertyChanged(nameof(HasName));
    }

    public void ClearStats()
    {
        Mmr = Games = WinRate = AverageRank = "—";
        Recent = "공식 API로 확인한 전적만 표시합니다";
    }

    public void Apply(LookupResult result)
    {
        Status = result.Message;
        if (result.Stats is not { } stats) return;
        Mmr = stats.Mmr.ToString("N0");
        Games = stats.Games.ToString("N0");
        WinRate = stats.Games == 0 ? "—" : $"{100.0 * stats.Wins / stats.Games:0.0}%";
        AverageRank = stats.AverageRank.ToString("0.0");
        Recent = stats.Recent;
    }
}
