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
var forbidden = await api.LookupAsync("이하링", "bad-key", 33, 2, default);
handler.Status = HttpStatusCode.TooManyRequests;
var limited = await api.LookupAsync("이하링", "test-key", 33, 2, default);
Assert(forbidden.Message != limited.Message && limited.Message.Contains("잠시 후"), "authentication and retryable rate limit are distinguished");
handler.Status = HttpStatusCode.NotFound;
Assert((await api.LookupAsync("없는사용자", "test-key", 33, 2, default)).Stats == null, "not found remains distinct from zero games");

/** 실제 게임 없는 연결과 매칭 알림의 중복 방지 검증 */
var attention = new GameAttention();
var now = DateTimeOffset.Parse("2026-10-16T12:00:00Z");
Assert(attention.Observe(false, MatchPhase.Lobby, 0, false, now) == GameNotice.None, "offline has no window request");
Assert(attention.Observe(true, MatchPhase.Lobby, 0, false, now) == GameNotice.Connected, "first connection requests one window display");
Assert(attention.Observe(true, MatchPhase.Lobby, 0, false, now) == GameNotice.None, "connection polling never repeats display");
Assert(attention.Observe(true, MatchPhase.Queuing, 1, false, now) == GameNotice.None, "queue alone does not claim a match");
Assert(attention.Observe(true, MatchPhase.Matched, 1, false, now) == GameNotice.None, "name capture gets time before window display");
Assert(attention.Observe(true, MatchPhase.Matched, 1, true, now.AddSeconds(2)) == GameNotice.MatchDetected, "confirmed roster triggers one match display");
Assert(attention.Observe(true, MatchPhase.Loading, 1, true, now.AddSeconds(3)) == GameNotice.None, "loading cannot repeat match display");
attention.Observe(true, MatchPhase.Matched, 2, false, now);
Assert(attention.Observe(true, MatchPhase.Matched, 2, false, now.AddSeconds(8)) == GameNotice.MatchDetected, "missing names still produce a bounded match notice");
Assert(attention.Observe(true, MatchPhase.InGame, 3, true, now.AddSeconds(10)) == GameNotice.None, "in-game updates do not raise the window");
attention.Observe(false, MatchPhase.Lobby, 3, false, now);
Assert(attention.Observe(true, MatchPhase.Matched, 5, true, now) == GameNotice.Connected && attention.Observe(true, MatchPhase.Matched, 5, true, now.AddSeconds(9)) == GameNotice.None, "reconnection shows once without replaying historical match");
attention.Observe(true, MatchPhase.Matched, 6, false, now);
attention.Observe(true, MatchPhase.Lobby, 6, false, now);
Assert(attention.Observe(true, MatchPhase.Matched, 6, true, now.AddSeconds(9)) == GameNotice.None, "cancelled selection does not replay its pending notice");

/** 오인식 확정과 누락 프레임의 연속 일치 회귀 검증 */
var names = new NicknameConsensus();
(int Slot, string Name, float Confidence)[] goodName = [(0, "정확이름", 0.93f)];
Assert(names.ObserveFrame(goodName, 3).Count == 0 && names.ObserveFrame(goodName, 3).Count == 0, "two frames cannot confirm identity");
Assert(names.ObserveFrame(goodName, 3).Single().Name == "정확이름", "third consistent frame confirms identity");
names.Reset();
names.ObserveFrame(goodName, 3);
names.ObserveFrame([], 3);
Assert(names.ObserveFrame(goodName, 3).Count == 0 && names.ObserveFrame(goodName, 3).Count == 0, "missing frame breaks consecutive evidence");
Assert(names.ObserveFrame(goodName, 3).Count == 1, "confirmation resumes after three fresh frames");
names.Reset();
for (var frame = 0; frame < 5; frame++)
    Assert(names.ObserveFrame([(0, "잘못이름", 0.30f)], 3).Count == 0, "low confidence never confirms frame " + frame);
names.Reset();
names.ObserveFrame(goodName, 3);
names.ObserveFrame([(0, "정확이름", float.NaN)], 3);
Assert(names.ObserveFrame(goodName, 3).Count == 0 && names.ObserveFrame(goodName, 3).Count == 0, "invalid confidence breaks evidence");
names.Reset();
Assert(names.ObserveFrame([goodName[0], goodName[0], goodName[0]], 3).Count == 0, "duplicate observations count as one frame");
names.ObserveFrame([(0, "정확이름", 0.93f), (0, "다른이름", 0.93f)], 3);
Assert(names.ObserveFrame(goodName, 3).Count == 0 && names.ObserveFrame(goodName, 3).Count == 0, "conflicting observations break evidence");
names.Reset();
for (var frame = 0; frame < 3; frame++) names.ObserveFrame([(1, "봇이름", 0.99f)], 1);
Assert(names.ObserveFrame([(1, "봇이름", 0.99f)], 1).Count == 0, "AI roster ignores extra slots");
names.ObserveFrame(goodName, 3);
names.Reset();
Assert(names.ObserveFrame(goodName, 3).Count == 0, "new match drops pending identity evidence");
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
