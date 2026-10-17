namespace EternalGG.Core;

/** 연속 프레임에서 확인한 닉네임 */
public sealed record ConfirmedNickname(int Slot, string Name);

/** 누락과 충돌을 제외하는 이름 연속 일치 판정 */
public sealed class NicknameConsensus
{
    public const float MinimumConfidence = 0.75f;
    public const int RequiredFrames = 3;
    private readonly Dictionary<int, (string Name, int Hits)> pending = [];

    /** 한 프레임당 슬롯별 한 번의 이름 증거 반영 */
    public IReadOnlyList<ConfirmedNickname> ObserveFrame(IEnumerable<(int Slot, string Name, float Confidence)> observations, int slots)
    {
        var frame = observations.Where(o => o.Slot >= 0 && o.Slot < slots).GroupBy(o => o.Slot).ToDictionary(g => g.Key, g => g.ToArray());
        var confirmed = new List<ConfirmedNickname>();
        for (var slot = 0; slot < slots; slot++)
        {
            if (!frame.TryGetValue(slot, out var values) || values.Select(v => v.Name).Distinct().Count() != 1 || values.Any(v => !float.IsFinite(v.Confidence) || v.Confidence < MinimumConfidence || NicknameRules.Normalize(v.Name) != v.Name))
            {
                pending.Remove(slot);
                continue;
            }

            var name = values[0].Name;
            var previous = pending.GetValueOrDefault(slot);
            var hits = previous.Name == name ? Math.Min(previous.Hits + 1, RequiredFrames) : 1;
            pending[slot] = (name, hits);
            if (hits == RequiredFrames) confirmed.Add(new(slot, name));
        }

        return confirmed;
    }

    /** 새 매칭과 인식 중단 시 미확정 증거 초기화 */
    public void Reset() => pending.Clear();
}
