using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace EternalGG.App.Converters;

/** 캐릭터와 티어의 동봉 리소스 로드 및 크기 제한 캐시 */
public sealed class PortraitConverter : IValueConverter
{
    private readonly Dictionary<string, BitmapImage?> cache = [];

    /** 잘못된 이미지의 문자 타일 대체를 위한 안전한 변환 */
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || !(path.StartsWith("Assets/Characters/", StringComparison.Ordinal) || path.StartsWith("Assets/Tiers/", StringComparison.Ordinal)) || !path.EndsWith(".png", StringComparison.Ordinal) || path.Contains("..") || path.Contains('\\') || path.Contains('?') || path.Contains('#')) return null;
        if (cache.TryGetValue(path, out var cached)) return cached;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 96;
            bitmap.UriSource = new Uri("pack://application:,,,/ErebusGG;component/" + path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            cache[path] = bitmap;
            return bitmap;
        }
        catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException)
        {
            cache[path] = null;
            return null;
        }
    }

    /** 표시 전용 변환의 역방향 차단 */
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}