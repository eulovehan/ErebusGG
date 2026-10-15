using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using EternalGG.Core;
using Tesseract;

namespace EternalGG.App.Services;

public sealed record NameRegion(double X, double Y, double Width, double Height);
public sealed record OcrCandidate(int Slot, string Name, float Confidence);

public sealed class OcrService : IDisposable
{
    private TesseractEngine? engine;
    public string Diagnostics { get; private set; } = "아직 읽지 않음";

    public IReadOnlyList<OcrCandidate> Read(Bitmap screenshot, IReadOnlyList<NameRegion> regions)
    {
        engine ??= new TesseractEngine(Path.Combine(AppContext.BaseDirectory, "tessdata"), "kor+eng", EngineMode.LstmOnly);
        engine.SetVariable("preserve_interword_spaces", "0");
        engine.SetVariable("user_defined_dpi", "200");
        var candidates = new List<OcrCandidate>();
        var diagnostics = new List<string>();
        for (var i = 0; i < regions.Count; i++)
        {
            var r = regions[i];
            var box = new Rectangle((int)(r.X * screenshot.Width), (int)(r.Y * screenshot.Height), (int)(r.Width * screenshot.Width), (int)(r.Height * screenshot.Height));
            box.Intersect(new Rectangle(Point.Empty, screenshot.Size));
            if (box.Width < 5 || box.Height < 5) continue;
            using var crop = new Bitmap(box.Width * 3 + 24, box.Height * 3 + 24, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(crop))
            {
                graphics.Clear(Color.FromArgb(20, 23, 30));
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(screenshot, new Rectangle(12, 12, box.Width * 3, box.Height * 3), box, GraphicsUnit.Pixel);
            }

            using var buffer = new MemoryStream();
            crop.Save(buffer, System.Drawing.Imaging.ImageFormat.Png);
            using var pix = Pix.LoadFromMemory(buffer.ToArray());
            using var page = engine.Process(pix, PageSegMode.SingleLine);
            var raw = page.GetText();
            diagnostics.Add($"영역 {i + 1}: {raw.Trim()} / {page.GetMeanConfidence():0.00}");
            var name = NicknameRules.Normalize(raw);
            if (name != null && page.GetMeanConfidence() >= 0.30f) candidates.Add(new(i, name, page.GetMeanConfidence()));
        }

        Diagnostics = string.Join(" · ", diagnostics);
        return candidates;
    }

    public void Dispose() => engine?.Dispose();
}
