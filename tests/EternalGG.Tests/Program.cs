using System.Net;
using System.Text;
using EternalGG.Core;

/** 검증 결과 집계 */
var count = 0;

/** 조건별 성공 출력과 실패 중단 */
void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + label);
    Console.WriteLine("PASS: " + label);
    count++;
}

/** AI 매칭과 재진입 상태 검증 */
var state = new MatchState();
state.Apply("[GlobalUserData:SetMatchingMode:142] Invoked: Test");
Assert(state.IsAi, "AI Test mode is recognized");
Assert(state.Phase == MatchPhase.Matched, "AI enters selection without normal queue events");
var aiGeneration = state.Generation;
state.Apply("[GlobalUserData:SetMatchingMode:142] Invoked: Test");
Assert(state.Generation == aiGeneration + 1, "AI selection restart without a cancel log creates a fresh roster");

/** 일반 매칭의 대기와 입장 및 로비 복귀 검증 */
state.IgnoreHistoricalPhase();
var baseline = state.Generation;
state.Apply("matchingNotification : StartMatching");
Assert(state.Phase == MatchPhase.Queuing && state.Generation == baseline + 1, "queue starts a new generation");
state.Apply("[OnReceiveMatchingUpdate] MatchingComplete, matchPadding.userCount : null");
Assert(state.Phase == MatchPhase.Matched, "match found with null count");
state.Apply("[PROFILE TIME][LOADING][GAME]");
state.Apply("[StartGame] FrameUpdateRate: 0.016");
Assert(state.Phase == MatchPhase.InGame, "loading then game start");
state.Apply("[PROFILE TIME][LOBBY]");
Assert(state.Phase == MatchPhase.Lobby, "return to lobby");
state.Apply("[GlobalUserData:SetMatchingMode:142] Invoked: Normal");
Assert(!state.IsAi, "normal mode clears AI flag");
state.Apply("matchingNotification : StartMatching");
Assert(state.Generation == baseline + 2, "next match clears previous generation");
state.Apply("matchingNotification : CancelMatching");
Assert(state.Phase == MatchPhase.Lobby, "queue cancellation");

/** 한국어와 영문 닉네임 및 잘못된 인식 결과 검증 */
Assert(NicknameRules.Normalize(" 이 하 링 \n") == "이하링", "Korean OCR whitespace normalization");
Assert(NicknameRules.Normalize("Player123") == "Player123", "mixed Latin nickname");
Assert(NicknameRules.Normalize("팀원 선택") == null, "UI text rejected");
Assert(NicknameRules.Normalize("name?invalid") == null, "invalid OCR punctuation rejected");

/** 키 누락 시 네트워크 차단 검증 */
var handler = new FakeHandler();
var api = new EternalReturnApi(new HttpClient(handler));
var noKey = await api.LookupAsync("이하링", "", 33, 2, default);
Assert(handler.Calls == 0 && noKey.Stats == null, "blank key makes zero network requests and no fake stats");

/** 공식 응답 형식과 조회 조건 검증 */
var result = await api.LookupAsync("이하링", "test-key", 33, 2, default);
Assert(result.Stats is { Games: 20, Wins: 4, AverageRank: 3.5 }, "official response fields parsed");
Assert(handler.Paths[0].Contains("nickname?query=") && handler.Paths[1] == "/v2/user/stats/uid/testuid/33/2", "UID endpoints and mode are used");
Assert(result.Stats!.Recent == "2위", "recent matches filtered by selected season and mode");

/** 잘못된 API 키의 오류 처리 검증 */
handler.Status = HttpStatusCode.Forbidden;
Assert((await api.LookupAsync("이하링", "bad-key", 33, 2, default)).Stats == null, "invalid key does not fabricate statistics");
Console.WriteLine($"{count} checks passed. HTTP fixtures only; no live API verification.");

/** 외부 통신 없는 고정 API 응답 */
sealed class FakeHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    public List<string> Paths { get; } = [];
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    /** 요청 경로 기록 및 테스트 응답 생성 */
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Paths.Add(request.RequestUri!.PathAndQuery);
        var json = request.RequestUri.AbsolutePath.Contains("nickname")
            ? """{"code":200,"user":{"uid":"testuid","nickname":"이하링"}}"""
            : request.RequestUri.AbsolutePath.Contains("stats")
                ? """{"code":200,"userStats":[{"matchingTeamMode":3,"totalGames":20,"totalWins":4,"mmr":0,"averageRank":3.5}]}"""
                : """{"code":200,"userGames":[{"seasonId":33,"matchingMode":2,"gameRank":2},{"seasonId":32,"matchingMode":3,"gameRank":1}]}""";
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
