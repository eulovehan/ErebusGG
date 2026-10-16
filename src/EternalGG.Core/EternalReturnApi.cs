using System.Net;
using System.Text.Json;

namespace EternalGG.Core;

/** 공식 전적 응답 모델 */
public sealed record PlayerStats(string Nickname, string Uid, int Games, int Wins, int Mmr, double AverageRank, string Recent, DateTimeOffset FetchedAt);

/** 전적 조회 결과와 상태 메시지 */
public sealed record LookupResult(PlayerStats? Stats, string Message);

/** 공식 API 전적 조회 클라이언트 */
public sealed class EternalReturnApi(HttpClient http)
{
    /** 닉네임 기반 사용자와 전적 조회 */
    public async Task<LookupResult> LookupAsync(string nickname, string apiKey, int season, int mode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return new(null, "현재 전적 조회를 사용할 수 없어요");
        try
        {
            /** 닉네임의 사용자 식별자 조회 */
            using var user = await GetAsync($"v1/user/nickname?query={Uri.EscapeDataString(nickname)}", apiKey, ct);
            var uid = user.RootElement.GetProperty("user").GetProperty("uid").GetString();
            if (string.IsNullOrWhiteSpace(uid)) return new(null, "사용자 식별 정보를 찾지 못했습니다");

            /** 선택 시즌과 모드의 스쿼드 전적 조회 */
            using var stats = await GetAsync($"v2/user/stats/uid/{Uri.EscapeDataString(uid)}/{season}/{mode}", apiKey, ct);
            var list = stats.RootElement.GetProperty("userStats");
            var value = list.EnumerateArray().FirstOrDefault(x => ReadInt(x, "matchingTeamMode") == 3);
            if (value.ValueKind != JsonValueKind.Object) return new(null, "선택 시즌/모드의 전적이 없습니다");

            /** 선택 조건에 맞는 최근 경기 요약 */
            var recent = "최근 경기 없음";
            using var games = await GetAsync($"v1/user/games/uid/{Uri.EscapeDataString(uid)}", apiKey, ct);
            if (games.RootElement.TryGetProperty("userGames", out var gameList))
            {
                var ranks = gameList.EnumerateArray().Where(x => ReadInt(x, "seasonId") == season && ReadInt(x, "matchingMode") == mode).Take(5).Select(x => $"{ReadInt(x, "gameRank")}위");
                recent = string.Join("  /  ", ranks);
                if (recent.Length == 0) recent = "선택 조건의 최근 경기 없음";
            }

            return new(new(nickname, uid, ReadInt(value, "totalGames"), ReadInt(value, "totalWins"), ReadInt(value, "mmr"), ReadDouble(value, "averageRank"), recent, DateTimeOffset.Now), "전적 조회 완료");
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound) { return new(null, "사용자 또는 전적을 찾지 못했습니다"); }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Forbidden) { return new(null, "현재 전적 조회를 사용할 수 없어요"); }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.TooManyRequests) { return new(null, "요청이 많아요. 잠시 후 다시 조회해 주세요."); }
        catch (OperationCanceledException) { return new(null, "조회가 지연되고 있어요. 다시 시도해 주세요."); }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException) { return new(null, "전적을 불러오지 못했어요. 다시 시도해 주세요."); }
    }

    /** 인증 헤더 전송 및 API 응답 코드 검증 */
    private async Task<JsonDocument> GetAsync(string path, string key, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri("https://open-api.bser.io/"), path));
        request.Headers.Add("x-api-key", key.Trim());
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (document.RootElement.TryGetProperty("code", out var code) && code.GetInt32() != 200)
        {
            var status = (HttpStatusCode)code.GetInt32();
            document.Dispose();
            throw new HttpRequestException("API response failure", null, status);
        }

        return document;
    }

    /** 정수 응답 필드 추출 */
    private static int ReadInt(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : 0;

    /** 실수 응답 필드 추출 */
    private static double ReadDouble(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) ? result : 0;
}
