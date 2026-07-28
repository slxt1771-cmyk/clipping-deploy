using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ClippingSoftware.App.Theming;

namespace ClippingSoftware.App.Converters;

/// <summary>
/// One-way: renders a hex text field's current value as a small preview swatch (Settings tab's Primary/
/// Secondary accent fields, M15) as the user types, before they click Apply. Falls back to transparent for
/// a partial/invalid hex mid-typing rather than throwing or showing a jarring error color.
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string hex && ColorUtils.TryParseHex(hex, out var color)
            ? new SolidColorBrush(color)
            : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
