namespace EternalGG.Core;

/** 한 번 표시할 게임 상태 알림 */
public enum GameNotice { None, Connected, MatchDetected }

/** 연결과 매칭별 창 표시의 중복 방지 */
public sealed class GameAttention
{
    private bool connected;
    private int seenGeneration;
    private int pendingGeneration = -1;
    private DateTimeOffset pendingSince;

    /** 이름 확인을 보존하는 전면 표시 시점 판정 */
    public GameNotice Observe(bool running, MatchPhase phase, int generation, bool namesReady, DateTimeOffset now)
    {
        if (!running)
        {
            connected = false;
            pendingGeneration = -1;
            return GameNotice.None;
        }

        if (!connected)
        {
            connected = true;
            seenGeneration = generation;
            pendingGeneration = -1;
            return GameNotice.Connected;
        }

        if (phase is not (MatchPhase.Matched or MatchPhase.Loading))
        {
            pendingGeneration = -1;
            return GameNotice.None;
        }

        if (generation != seenGeneration)
        {
            seenGeneration = pendingGeneration = generation;
            pendingSince = now;
        }

        if (pendingGeneration != generation || !namesReady && now - pendingSince < TimeSpan.FromSeconds(8))
            return GameNotice.None;
        pendingGeneration = -1;
        return GameNotice.MatchDetected;
    }
}
