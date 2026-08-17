using System.Globalization;
using System.Windows.Data;

namespace ClippingSoftware.App.Converters;

/// <summary>Plain bool negation - used for IsEnabled bindings that should be the opposite of some other
/// bound flag (e.g. a manual field that's disabled while its "auto" counterpart is checked). Distinct from
/// InverseBooleanToVisibilityConverter, which targets Visibility rather than another bool.</summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? false : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? false : true;
}
