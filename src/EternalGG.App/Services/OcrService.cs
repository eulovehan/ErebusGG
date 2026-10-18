using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using EternalGG.Core;
using Tesseract;

namespace EternalGG.App.Services;

/** 해상도 비율 기반 이름 영역 */
public sealed record NameRegion(double X, double Y, double Width, double Height);

/** 슬롯별 이름 인식 후보 */
public sealed record OcrCandidate(int Slot, string Name, float Confidence, bool RequiresReview = false);

/** 한국어와 일본어 및 영중문 로컬 이름 인식 */
public sealed class OcrService : IDisposable
{
    private TesseractEngine? engine;
    private TesseractEngine? koreanVerifier;
    private readonly PaddleNicknameRecognizer multilingual = new();
    public string Diagnostics { get; private set; } = "아직 읽지 않음";

    /** 이름 띠와 두 전처리 결과가 일치한 후보 추출 */
    public IReadOnlyList<OcrCandidate> Read(Bitmap screenshot, IReadOnlyList<NameRegion> regions, bool requireNameplate = true)
    {
        engine ??= new TesseractEngine(Path.Combine(AppContext.BaseDirectory, "tessdata"), "kor+eng", EngineMode.LstmOnly);
        engine.SetVariable("preserve_interword_spaces", "0");
        engine.SetVariable("user_defined_dpi", "300");
        var candidates = new List<OcrCandidate>();
        var diagnostics = new List<string>();
        for (var i = 0; i < regions.Count; i++)
        {
            var r = regions[i];
            var box = new Rectangle((int)(r.X * screenshot.Width), (int)(r.Y * screenshot.Height), (int)(r.Width * screenshot.Width), (int)(r.Height * screenshot.Height));
            box.Intersect(new Rectangle(Point.Empty, screenshot.Size));
            if (box.Width < 5 || box.Height < 5) continue;
            if (requireNameplate && !HasNameplate(screenshot, box))
            {
                diagnostics.Add($"영역 {i + 1}: 이름 띠 확인 대기");
                continue;
            }

            /** 테두리와 팀 색상 띠를 제외한 밝은 글자 범위 */
            var ink = FindInk(screenshot, box);
            if (ink.IsEmpty)
            {
                diagnostics.Add($"영역 {i + 1}: 읽을 이름 없음");
                continue;
            }

            /** 닉네임 띠의 오른쪽 정렬과 다른 위치의 문자 제외 */
            if (requireNameplate && box.Right - ink.Right > Math.Max(8, box.Height / 2))
            {
                diagnostics.Add($"영역 {i + 1}: 이름 위치 확인 대기");
                continue;
            }

            var gray = Recognize(screenshot, ink, true, 3);
            var color = Recognize(screenshot, ink, false, 4);
            diagnostics.Add($"영역 {i + 1}: 명암 {gray.Name ?? "없음"} / {gray.Confidence:0.00}, 원색 {color.Name ?? "없음"} / {color.Confidence:0.00}");

            /** 다국어 모델 검증과 독립 인식 증거의 충돌 해소 */
            var corroboration = gray.Name != null && gray.Name == color.Name && Math.Min(gray.Confidence, color.Confidence) >= NicknameConsensus.MinimumConfidence ? gray.Name : null;
            var verified = multilingual.Read(screenshot, ink, corroboration);
            diagnostics.Add($"영역 {i + 1} 다국어: {verified.Detail}");

            /** 한국어 받침 충돌 시 다른 학습 모델의 독립 재검증 */
            if (verified.Name != corroboration && corroboration != null && corroboration.Any(c => c is >= '\uac00' and <= '\ud7a3') && (verified.Name == null || verified.Name.Any(c => c is >= '\uac00' and <= '\ud7a3')))
            {
                koreanVerifier ??= new TesseractEngine(Path.Combine(AppContext.BaseDirectory, "tessdata"), "kor_best+eng", EngineMode.LstmOnly);
                var extraGray = Recognize(screenshot, ink, true, 3, koreanVerifier);
                var extraColor = Recognize(screenshot, ink, false, 4, koreanVerifier);
                diagnostics.Add($"영역 {i + 1} 한국어 재검증: {extraGray.Name ?? "없음"} / {extraGray.Confidence:0.00}, {extraColor.Name ?? "없음"} / {extraColor.Confidence:0.00}");
                if (extraGray.Name == corroboration && extraColor.Name == corroboration && Math.Min(extraGray.Confidence, extraColor.Confidence) >= 0.90f)
                    candidates.Add(new(i, corroboration, Math.Min(extraGray.Confidence, extraColor.Confidence)));
                else if (verified.Name != null) candidates.Add(new(i, verified.Name, verified.Confidence, true));
            }
            else if (verified.Name != null) candidates.Add(new(i, verified.Name, verified.Confidence, verified.RequiresReview));

            /** 다국어 모델의 결과가 없을 때도 읽힌 후보의 확인 필요 표시 */
            if (!candidates.Any(c => c.Slot == i))
            {
                var fallback = new[] { gray, color }.Where(c => c.Name != null).OrderByDescending(c => c.Confidence).ToArray();
                if (fallback.Length > 0) candidates.Add(new(i, fallback[0].Name!, fallback[0].Confidence, true));
            }
        }

        Diagnostics = string.Join(" / ", diagnostics);
        return candidates;
    }

    /** 선택 화면의 팀 색상 이름 띠 확인 */
    private static bool HasNameplate(Bitmap image, Rectangle box)
    {
        var bandHeight = Math.Max(2, box.Height / 5);
        for (var y = box.Bottom - bandHeight; y < box.Bottom; y++)
        {
            var colored = 0;
            for (var x = box.Left; x < box.Right; x++)
            {
                var c = image.GetPixel(x, y);
                var high = Math.Max(c.R, Math.Max(c.G, c.B));
                var low = Math.Min(c.R, Math.Min(c.G, c.B));
                if (high >= 100 && high - low >= 45) colored++;
            }

            if (colored >= box.Width * 0.65) return true;
        }

        return false;
    }

    /** 중립색 글자의 실제 외곽 탐색 */
    private static Rectangle FindInk(Bitmap image, Rectangle box)
    {
        /** 오른쪽 이름과 멀리 떨어진 카드 구분선 분리 */
        var gap = 0;
        var start = box.Right;
        var found = false;
        for (var x = box.Right - 1; x >= box.Left; x--)
        {
            var hasInk = false;
            for (var y = box.Top; y < box.Bottom; y++)
            {
                var c = image.GetPixel(x, y);
                if (Math.Min(c.R, Math.Min(c.G, c.B)) >= 130 && Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) <= 50)
                {
                    hasInk = true;
                    break;
                }
            }

            if (hasInk) { found = true; gap = 0; start = x; }
            else if (found && ++gap > Math.Max(6, box.Height / 2)) break;
        }

        if (!found) return Rectangle.Empty;
        box = Rectangle.FromLTRB(start, box.Top, box.Right, box.Bottom);
        var left = box.Right;
        var top = box.Bottom;
        var right = box.Left - 1;
        var bottom = box.Top - 1;
        for (var y = box.Top; y < box.Bottom; y++)
        {
            for (var x = box.Left; x < box.Right; x++)
            {
                var c = image.GetPixel(x, y);
                var high = Math.Max(c.R, Math.Max(c.G, c.B));
                var low = Math.Min(c.R, Math.Min(c.G, c.B));
                if (low < 130 || high - low > 50) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        return right - left < 3 || bottom - top < 3 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    /** 글자 가장자리를 보존하는 명암 변환과 확대 */
    private (string? Name, float Confidence) Recognize(Bitmap image, Rectangle ink, bool grayscale, int scale, TesseractEngine? recognizer = null)
    {
        using var text = new Bitmap(ink.Width, ink.Height, PixelFormat.Format24bppRgb);
        for (var y = 0; y < ink.Height; y++)
        {
            for (var x = 0; x < ink.Width; x++)
            {
                var c = image.GetPixel(ink.X + x, ink.Y + y);
                if (grayscale)
                {
                    var high = Math.Max(c.R, Math.Max(c.G, c.B));
                    var low = Math.Min(c.R, Math.Min(c.G, c.B));
                    var value = high - low > 60 ? 255 : 255 - Math.Clamp((low - 65) * 255 / 190, 0, 255);
                    c = Color.FromArgb(value, value, value);
                }

                text.SetPixel(x, y, c);
            }
        }

        using var crop = new Bitmap(text.Width * scale + 24, text.Height * scale + 24, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(crop))
        {
            graphics.Clear(grayscale ? Color.White : Color.FromArgb(20, 23, 30));
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(text, new Rectangle(12, 12, text.Width * scale, text.Height * scale), new Rectangle(Point.Empty, text.Size), GraphicsUnit.Pixel);
        }

        using var buffer = new MemoryStream();
        crop.Save(buffer, System.Drawing.Imaging.ImageFormat.Png);
        using var pix = Pix.LoadFromMemory(buffer.ToArray());
        using var page = (recognizer ?? engine!).Process(pix, PageSegMode.SingleLine);
        return (NicknameRules.Normalize(page.GetText()), page.GetMeanConfidence());
    }

    /** 네이티브 인식 엔진 해제 */
    public void Dispose()
    {
        engine?.Dispose();
        koreanVerifier?.Dispose();
        multilingual.Dispose();
    }
}
