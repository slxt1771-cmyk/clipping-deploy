using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ClippingSoftware.App.Theming;

namespace ClippingSoftware.App.Converters;

/// <summary>
/// One-way: picks black or white text (see ColorUtils.GetReadableForeground) for a hex text field's
/// current value, so text drawn on a user-chosen background (tag chips) stays legible regardless of how
/// dark or light that color is. Falls back to white for a partial/invalid hex mid-typing, matching
/// HexToBrushConverter's tolerant behavior.
/// </summary>
public class HexToReadableForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string hex && ColorUtils.TryParseHex(hex, out var color)
            ? new SolidColorBrush(ColorUtils.GetReadableForeground(color))
            : Brushes.White;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
