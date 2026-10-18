using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using EternalGG.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace EternalGG.App.Services;

/** 다국어 이름의 로컬 모델 검증 */
internal sealed class PaddleNicknameRecognizer : IDisposable
{
    private readonly Dictionary<string, (InferenceSession Session, string[] Characters)> models = [];

    /** 두 명암 영상과 문자권이 일치한 보완 후보 */
    public (string? Name, float Confidence, string Detail) Read(Bitmap image, Rectangle ink, string? corroboration)
    {
        var candidates = new List<(string Name, float Confidence)>();
        var details = new List<string>();
        foreach (var language in new[] { "korean", "ch" })
        {
            var normal = Recognize(image, ink, language, false);
            var inverted = Recognize(image, ink, language, true);
            details.Add($"{language} {normal.Name ?? "없음"} / {normal.Confidence:0.0000}, 반전 {inverted.Name ?? "없음"} / {inverted.Confidence:0.0000}");

            if (normal.Name == null || normal.Name != inverted.Name) continue;
            var hasHangul = normal.Name.Any(c => c is >= '\uac00' and <= '\ud7a3');
            if (language == "ch" && hasHangul || language == "korean" && !hasHangul && normal.Name.Any(c => c > 127)) continue;
            var confidence = Math.Min(normal.Confidence, inverted.Confidence);

            /** 색상 배경의 경계 점수 후보에 대한 명암 재검증 */
            if (confidence is >= 0.85f and < 0.90f)
            {
                var neutral = Recognize(image, ink, language, false, true);
                var neutralInverted = Recognize(image, ink, language, true, true);
                details.Add($"명암 {neutral.Name ?? "없음"} / {neutral.Confidence:0.0000}, 명암 반전 {neutralInverted.Name ?? "없음"} / {neutralInverted.Confidence:0.0000}");
                if (neutral.Name != normal.Name || neutralInverted.Name != normal.Name) continue;
                confidence = Math.Min(neutral.Confidence, neutralInverted.Confidence);
            }

            if (confidence >= 0.90f) candidates.Add((normal.Name, confidence));
        }

        /** 다른 문자권에서 앞부분이 탈락한 영숫자 후보 제외 */
        var complete = candidates.Where(c => !candidates.Any(other => other.Name.Length > c.Name.Length && other.Name.EndsWith(c.Name, StringComparison.Ordinal)))
            .Where(c => c.Name.Any(ch => ch > 127) || c.Name == corroboration || !candidates.Any(other => other.Name.Any(ch => ch > 127)));
        var ranked = complete.GroupBy(c => c.Name).Select(g => (Name: g.Key, Confidence: g.Max(c => c.Confidence))).OrderByDescending(c => c.Confidence).ToArray();
        if (ranked.Length == 1 || ranked.Length > 1 && ranked[0].Confidence - ranked[1].Confidence >= 0.02f)
            return (ranked[0].Name, ranked[0].Confidence, string.Join(" / ", details));
        var supported = ranked.Where(c => c.Name == corroboration).ToArray();
        return supported.Length == 1
            ? (supported[0].Name, supported[0].Confidence, string.Join(" / ", details))
            : (null, 0, string.Join(" / ", details));
    }

    /** 비율 보존 BGR 정규화와 CTC 중복 문자 복원 */
    private (string? Name, float Confidence) Recognize(Bitmap image, Rectangle ink, string language, bool invert, bool neutral = false)
    {
        var model = GetModel(language);
        const int height = 48;
        const int padding = 2;
        var scaledWidth = (int)Math.Ceiling((ink.Width + 2.0 * padding) * height / (ink.Height + 2.0 * padding));
        var width = Math.Max(320, (scaledWidth + 7) / 8 * 8);
        if (width > 2048) return (null, 0);
        using var crop = new Bitmap(ink.Width + 2 * padding, ink.Height + 2 * padding);
        using (var graphics = Graphics.FromImage(crop))
        {
            graphics.Clear(Color.FromArgb(25, 27, 30));
            graphics.DrawImage(image, new Rectangle(padding, padding, ink.Width, ink.Height), ink, GraphicsUnit.Pixel);
        }

        /** 작은 밝은 글자의 획을 유지하는 색상 제거 */
        if (neutral)
        {
            for (var y = padding; y < padding + ink.Height; y++)
            {
                for (var x = padding; x < padding + ink.Width; x++)
                {
                    var c = crop.GetPixel(x, y);
                    var value = Math.Min(c.R, Math.Min(c.G, c.B));
                    crop.SetPixel(x, y, Color.FromArgb(value, value, value));
                }
            }
        }

        using var resized = new Bitmap(scaledWidth, height);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(crop, new Rectangle(0, 0, scaledWidth, height));
        }

        var input = new DenseTensor<float>(new[] { 1, 3, height, width });
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < scaledWidth; x++)
            {
                var c = resized.GetPixel(x, y);
                input[0, 0, y, x] = (invert ? 255 - c.B : c.B) / 127.5f - 1;
                input[0, 1, y, x] = (invert ? 255 - c.G : c.G) / 127.5f - 1;
                input[0, 2, y, x] = (invert ? 255 - c.R : c.R) / 127.5f - 1;
            }
        }

        using var output = model.Session.Run([NamedOnnxValue.CreateFromTensor(model.Session.InputNames[0], input)]);
        var probabilities = output.First().AsTensor<float>();
        var text = new StringBuilder();
        var confidence = 0f;
        var count = 0;
        var previous = 0;
        if (probabilities.Dimensions[2] != model.Characters.Length) throw new InvalidDataException("인식 모델 문자사전 불일치");
        for (var step = 0; step < probabilities.Dimensions[1]; step++)
        {
            var best = 0;
            var score = probabilities[0, step, 0];
            for (var index = 1; index < probabilities.Dimensions[2]; index++)
            {
                var value = probabilities[0, step, index];
                if (value > score) { best = index; score = value; }
            }

            if (best != 0 && best != previous)
            {
                text.Append(model.Characters[best]);
                confidence += score;
                count++;
            }

            previous = best;
        }

        return (NicknameRules.Normalize(text.ToString()), count == 0 ? 0 : confidence / count);
    }

    /** 필요한 언어 모델의 지연 로드와 CPU 사용 제한 */
    private (InferenceSession Session, string[] Characters) GetModel(string language)
    {
        if (models.TryGetValue(language, out var model)) return model;
        using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
        var session = new InferenceSession(Path.Combine(AppContext.BaseDirectory, "models", language + "_PP-OCRv5_rec_mobile.onnx"), options);
        try
        {
            var characters = new[] { "" }.Concat(session.ModelMetadata.CustomMetadataMap["character"].TrimEnd('\r', '\n').Split('\n').Select(c => c.TrimEnd('\r'))).Append(" ").ToArray();
            model = (session, characters);
            models.Add(language, model);
            return model;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /** 네이티브 모델 메모리 해제 */
    public void Dispose()
    {
        foreach (var model in models.Values) model.Session.Dispose();
        models.Clear();
    }
}
