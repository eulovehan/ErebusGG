using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace EternalGG.App.Services;

public static class GameCapture
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();

    public static nint FindGame()
    {
        foreach (var process in Process.GetProcessesByName("EternalReturn"))
        {
            using (process)
            {
                if (process.MainWindowHandle != 0) return process.MainWindowHandle;
            }
        }

        return 0;
    }

    public static Bitmap? CaptureForeground()
    {
        var window = FindGame();
        if (window == 0 || GetForegroundWindow() != window) return null;
        if (!GetClientRect(window, out var rect) || rect.Right < 640 || rect.Bottom < 360) return null;
        var point = new Point(0, 0);
        ClientToScreen(window, ref point);
        var bitmap = new Bitmap(rect.Right, rect.Bottom, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(point, Point.Empty, bitmap.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }
}
