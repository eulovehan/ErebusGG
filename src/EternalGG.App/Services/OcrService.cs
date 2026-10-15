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

    /** 지정 영역의 닉네임 후보 추출 */
    public IReadOnlyList<OcrCandidate> Read(Bitmap screenshot, IReadOnlyList<NameRegion> regions)
    {
        /** 로컬 언어 모델 초기화 및 엔진 재사용 */
        engine ??= new TesseractEngine(Path.Combine(AppContext.BaseDirectory, "tessdata"), "kor+eng", EngineMode.LstmOnly);
        engine.SetVariable("preserve_interword_spaces", "0");
        engine.SetVariable("user_defined_dpi", "200");
        var candidates = new List<OcrCandidate>();
        var diagnostics = new List<string>();

        /** 슬롯별 이름 영역 자르기 */
        for (var i = 0; i < regions.Count; i++)
        {
            var r = regions[i];
            var box = new Rectangle((int)(r.X * screenshot.Width), (int)(r.Y * screenshot.Height), (int)(r.Width * screenshot.Width), (int)(r.Height * screenshot.Height));
            box.Intersect(new Rectangle(Point.Empty, screenshot.Size));
            if (box.Width < 5 || box.Height < 5) continue;

            /** 작은 글자 확대 및 인식용 여백 추가 */
            using var crop = new Bitmap(box.Width * 3 + 24, box.Height * 3 + 24, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(crop))
            {
                graphics.Clear(Color.FromArgb(20, 23, 30));
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(screenshot, new Rectangle(12, 12, box.Width * 3, box.Height * 3), box, GraphicsUnit.Pixel);
            }

            /** 한 줄 문자 인식 및 최소 점수 검증 */
            using var buffer = new MemoryStream();
            crop.Save(buffer, System.Drawing.Imaging.ImageFormat.Png);
            using var pix = Pix.LoadFromMemory(buffer.ToArray());
            using var page = engine.Process(pix, PageSegMode.SingleLine);
            var raw = page.GetText();
            diagnostics.Add($"영역 {i + 1}: {raw.Trim()} / {page.GetMeanConfidence():0.00}");
            var name = NicknameRules.Normalize(raw);
            if (name != null && page.GetMeanConfidence() >= 0.30f) candidates.Add(new(i, name, page.GetMeanConfidence()));
        }

        /** 로컬 인식 진단 결과 요약 */
        Diagnostics = string.Join(" · ", diagnostics);
        return candidates;
    }

    /** 네이티브 인식 엔진 해제 */
    public void Dispose() => engine?.Dispose();
}
