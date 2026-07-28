using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ClippingSoftware.App.Converters;

/// <summary>Opposite of the built-in BooleanToVisibilityConverter (true -> Collapsed, false -> Visible) -
/// used for a placeholder that should show only while some "has a selection" flag is false, paired with the
/// built-in converter on the counterpart panel that shows once it's true (TagManagerView).</summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
