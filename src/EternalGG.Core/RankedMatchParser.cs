using System.Globalization;
using System.Text.Json;

namespace EternalGG.Core;

/** 공식 최근 경기 응답과 초상화 코드의 연결 */
public static class RankedMatchParser
{
    /** 충분한 필드가 있는 랭크 스쿼드 경기의 정규화 */
    public static RecentRanked? Parse(JsonElement games)
    {
        if (games.ValueKind != JsonValueKind.Array) return null;
        var matches = new List<RankedMatch>();
        foreach (var game in games.EnumerateArray())
        {
            if (!Number(game, "matchingMode", out var mode)) return null;
            if (mode != 3) continue;
            if (!Number(game, "matchingTeamMode", out var team)) return null;
            if (team != 3) continue;
            if (!Number(game, "gameRank", out var rank) || !Number(game, "characterNum", out var code) || code < 1) return null;
            if (rank < 1) continue;
            if (!game.TryGetProperty("gameId", out var id) || id.ValueKind is not (JsonValueKind.Number or JsonValueKind.String)) return null;
            var gameId = id.ValueKind == JsonValueKind.String ? id.GetString() : id.GetRawText();
            if (string.IsNullOrWhiteSpace(gameId)) return null;
            if (!game.TryGetProperty("startDtm", out var time) || time.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(time.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var playedAt)) return null;
            var character = CharacterCatalog.Find(code)?.Name ?? "알 수 없는 캐릭터";
            matches.Add(new(gameId, playedAt, character, rank, mode, team, code));
        }

        return RecentRanked.FromGames(matches);
    }

    /** 응답 숫자 필드의 형식 검증 */
    private static bool Number(JsonElement value, string field, out int number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(field, out var property) &&
            property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out number);
    }
}
