using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace ClippingSoftware.App.Converters;

/// <summary>
/// Converts a clip thumbnail file path into a <see cref="BitmapImage"/> for binding to an
/// &lt;Image&gt;. Returns null (view shows its placeholder) when the path is null/empty or the
/// file doesn't exist, so a missing/not-yet-generated thumbnail never crashes the browser.
///
/// Loads with CacheOption=OnLoad and freezes the result so the file handle is released immediately
/// after decode (no lock held on the thumbnail file) and the image is safe to hand back on any thread.
/// </summary>
public class ThumbnailPathToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            // Corrupt/partially-written thumbnail file - fall back to the placeholder rather than crash.
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
