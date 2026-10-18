using System.Text.Json;

namespace EternalGG.Core;

/** 캐릭터 코드와 동봉 초상화의 대응 정보 */
public sealed record CharacterDefinition(int Code, string Key, string Name, string Portrait);

/** 외부 요청 없는 캐릭터 식별과 초상화 경로 조회 */
public static class CharacterCatalog
{
    public static IReadOnlyList<CharacterDefinition> All { get; } = Load();
    private static readonly Dictionary<int, CharacterDefinition> ByCode = All.ToDictionary(c => c.Code);
    private static readonly Dictionary<string, CharacterDefinition> ByName = All
        .SelectMany(c => new[] { KeyValuePair.Create(Normalize(c.Name), c), KeyValuePair.Create(Normalize(c.Key), c) })
        .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First().Value);

    /** 이름보다 API 코드 우선의 안전한 캐릭터 조회 */
    public static CharacterDefinition? Find(int code, string? name = null)
    {
        if (code > 0) return ByCode.GetValueOrDefault(code);
        return ByName.GetValueOrDefault(Normalize(name ?? ""));
    }

    /** 공백과 연결 기호를 제외한 이름 대응 */
    private static string Normalize(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    /** 검증된 내장 매니페스트의 일회 로드 */
    private static IReadOnlyList<CharacterDefinition> Load()
    {
        using var stream = typeof(CharacterCatalog).Assembly.GetManifestResourceStream("EternalGG.Characters.json")
            ?? throw new InvalidOperationException("캐릭터 목록 누락");
        using var document = JsonDocument.Parse(stream);
        var entries = document.RootElement.GetProperty("characters").Deserialize<CharacterDefinition[]>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("캐릭터 목록 형식 오류");
        return Array.AsReadOnly(entries);
    }
}
