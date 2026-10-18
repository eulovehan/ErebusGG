namespace EternalGG.Core;

/** 최근 랭크 집계용 경기 계약 */
public sealed record RankedMatch(string GameId, DateTimeOffset PlayedAt, string Character, int Rank, int Mode = 3, int TeamMode = 3, int CharacterCode = 0);

/** 동일 표본의 캐릭터별 사용 기록 */
public sealed record MostPick(string Character, int Games, int Wins, int CharacterCode = 0)
{
    public string DisplayName => CharacterCatalog.Find(CharacterCode, Character)?.Name ?? Character;
    public string? PortraitPath => CharacterCatalog.Find(CharacterCode, Character)?.Portrait;
    public string Initial => System.Globalization.StringInfo.GetNextTextElement(DisplayName);
    public string Summary => $"{Games}경기 / {100.0 * Wins / Games:0}%";
}

/** 최근 랭크 최대 스무 경기의 중복 없는 집계 */
public sealed record RecentRanked(int Games, int Wins, IReadOnlyList<MostPick> MostPicks)
{
    public string WinRate => Games == 0 ? "—" : $"{100.0 * Wins / Games:0}%";
    public string Summary => Games == 0 ? "랭크 기록 없음" : $"{Games}경기 / {Wins}승 {Games - Wins}패";

    /** 랭크 스쿼드 필터와 최신 표본 선택 */
    public static RecentRanked FromGames(IEnumerable<RankedMatch> games)
    {
        var sample = games.Where(g => g.Mode == 3 && g.TeamMode == 3 && g.Rank > 0 && !string.IsNullOrWhiteSpace(g.GameId))
            .OrderByDescending(g => g.PlayedAt).ThenBy(g => g.GameId, StringComparer.Ordinal)
            .DistinctBy(g => g.GameId).Take(20).ToArray();
        var picks = sample.Where(g => g.CharacterCode > 0 || !string.IsNullOrWhiteSpace(g.Character))
            .Select(g => g with { CharacterCode = CharacterCatalog.Find(g.CharacterCode, g.Character)?.Code ?? g.CharacterCode })
            .GroupBy(g => g.CharacterCode > 0 ? $"code:{g.CharacterCode}" : "name:" + g.Character)
            .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Max(x => x.PlayedAt)).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(3).Select(g => new MostPick(CharacterCatalog.Find(g.First().CharacterCode, g.First().Character)?.Name ?? g.First().Character,
                g.Count(), g.Count(x => x.Rank == 1), g.First().CharacterCode)).ToArray();
        return new(sample.Length, sample.Count(g => g.Rank == 1), picks);
    }
}
