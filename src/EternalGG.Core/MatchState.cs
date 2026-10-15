using System.Text.RegularExpressions;

namespace EternalGG.Core;

/** 매칭 진행 단계 */
public enum MatchPhase { Offline, Lobby, Queuing, Matched, Loading, InGame, Finished }

/** 로그 기반 매칭 상태 */
public sealed partial class MatchState
{
    public MatchPhase Phase { get; private set; } = MatchPhase.Lobby;
    public bool IsAi { get; private set; }
    public int Generation { get; private set; }
    public string Mode { get; private set; } = "모드 확인 중";

    /** 로그 한 줄의 상태 변경 반영 */
    public bool Apply(string line)
    {
        var before = (Phase, IsAi, Mode, Generation);

        /** 대전 모드 판별 및 AI 선택 화면 진입 */
        if (line.Contains("GlobalUserData:SetMatchingMode", StringComparison.Ordinal))
        {
            if (line.Contains("Invoked: Test", StringComparison.Ordinal))
            {
                IsAi = true;
                Mode = "AI 상대 대전";
                Phase = MatchPhase.Matched;
                Generation++;
            }
            else if (line.Contains("Invoked: Normal", StringComparison.Ordinal)) { IsAi = false; Mode = "일반 대전"; }
            else if (line.Contains("Invoked: Rank", StringComparison.Ordinal)) { IsAi = false; Mode = "랭크 대전"; }
        }

        /** 매칭 알림과 화면 전환별 단계 갱신 */
        if (line.Contains("matchingNotification : StartMatching", StringComparison.Ordinal))
        {
            Phase = MatchPhase.Queuing;
            Generation++;
        }
        else if (line.Contains("] MatchingComplete,", StringComparison.Ordinal))
        {
            Phase = MatchPhase.Matched;
        }
        else if (line.Contains("[PROFILE TIME][LOADING][GAME]", StringComparison.Ordinal))
        {
            if (Phase is MatchPhase.Lobby or MatchPhase.Finished) Generation++;
            Phase = MatchPhase.Loading;
        }
        else if (line.Contains("[StartGame] FrameUpdateRate", StringComparison.Ordinal)) Phase = MatchPhase.InGame;
        else if (line.Contains("[PROFILE TIME][LOBBY]", StringComparison.Ordinal)) Phase = MatchPhase.Lobby;
        else if (line.Contains("GameResultTeamPlayerSlot", StringComparison.Ordinal)) Phase = MatchPhase.Finished;
        else if (line.Contains("matchingNotification : Cancel", StringComparison.Ordinal)) Phase = MatchPhase.Lobby;
        return before != (Phase, IsAi, Mode, Generation);
    }

    /** 게임 종료 후 매칭 상태 초기화 */
    public void Reset()
    {
        Phase = MatchPhase.Lobby;
        IsAi = false;
        Mode = "모드 확인 중";
        Generation++;
    }

    /** 과거 로그의 진행 단계 제외 */
    public void IgnoreHistoricalPhase() => Phase = MatchPhase.Lobby;
}

/** 닉네임 인식 결과 검증 */
public static partial class NicknameRules
{
    /** 닉네임 허용 문자와 길이 */
    [GeneratedRegex(@"^[\p{L}\p{N}_]{2,24}$")]
    private static partial Regex ValidName();

    /** 인식 공백 제거 및 화면 문구 제외 */
    public static string? Normalize(string raw)
    {
        var value = raw.Trim().Replace(" ", "");
        if (!ValidName().IsMatch(value)) return null;
        if (new[] { "플레이어", "매칭", "준비", "팀원", "실험체", "선택", "스쿼드", "BOT", "Bot", "AI" }.Any(value.Contains)) return null;
        return value;
    }
}
