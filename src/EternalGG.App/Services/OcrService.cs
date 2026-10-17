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
public sealed record OcrCandidate(int Slot, string Name, float Confidence);

/** 한국어와 영어 로컬 이름 인식 */
public sealed class OcrService : IDisposable
{
    private TesseractEngine? engine;
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
            if (gray.Name != null && gray.Name == color.Name && Math.Min(gray.Confidence, color.Confidence) >= NicknameConsensus.MinimumConfidence)
                candidates.Add(new(i, gray.Name, Math.Min(gray.Confidence, color.Confidence)));
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
    private (string? Name, float Confidence) Recognize(Bitmap image, Rectangle ink, bool grayscale, int scale)
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
        using var page = engine!.Process(pix, PageSegMode.SingleLine);
        return (NicknameRules.Normalize(page.GetText()), page.GetMeanConfidence());
    }

    /** 네이티브 인식 엔진 해제 */
    public void Dispose() => engine?.Dispose();
}
