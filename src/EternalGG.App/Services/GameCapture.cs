using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace EternalGG.App.Services;

/** 게임 창 탐색과 전경 화면 캡처 */
public static class GameCapture
{
    /** 윈도우 클라이언트 영역 좌표 */
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    /** 게임 화면 위치와 전경 창 확인용 윈도우 함수 */
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();

    /** 실행 중인 게임의 주 창 탐색 */
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

    /** 전경 게임 창의 클라이언트 영역 캡처 */
    public static Bitmap? CaptureForeground()
    {
        var window = FindGame();
        if (window == 0 || GetForegroundWindow() != window) return null;
        if (!GetClientRect(window, out var rect) || rect.Right < 640 || rect.Bottom < 360) return null;

        /** 클라이언트 좌표의 화면 좌표 변환 */
        var point = new Point(0, 0);
        ClientToScreen(window, ref point);
        var bitmap = new Bitmap(rect.Right, rect.Bottom, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(point, Point.Empty, bitmap.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }
}
