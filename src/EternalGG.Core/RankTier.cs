namespace EternalGG.Core;

/** 공식 시즌과 모드가 확인된 티어 표시 계약 */
public sealed record RankTier(int SeasonId, string SeasonName, int TierId, int? Division = null, int? Points = null, bool IsUnranked = false)
{
    /** 알려진 티어의 원본 이미지와 이름 연결 */
    public static IReadOnlyDictionary<int, string> Names { get; } = new System.Collections.ObjectModel.ReadOnlyDictionary<int, string>(new Dictionary<int, string>
    {
        [1] = "아이언", [2] = "브론즈", [3] = "실버", [4] = "골드", [5] = "플래티넘",
        [6] = "다이아몬드", [63] = "메테오라이트", [66] = "미스릴", [7] = "데미갓", [8] = "이터니티"
    });

    /** 미제공과 배치 전 기록을 구분한 티어 이름 */
    public string DisplayName => IsUnranked ? "언랭크" : Names.TryGetValue(TierId, out var name)
        ? name + (TierId is 1 or 2 or 3 or 4 or 5 or 6 or 63 && Division is >= 1 and <= 4 ? " " + Division : "") : "티어 정보 없음";
    public string? PortraitPath => !IsUnranked && Names.ContainsKey(TierId) ? $"Assets/Tiers/{TierId}.png" : null;
    public string Detail => string.Join(" / ", new[] { SeasonName, Points is >= 0 && !IsUnranked ? $"{Points:N0} RP" : "" }.Where(x => !string.IsNullOrWhiteSpace(x)));
}
