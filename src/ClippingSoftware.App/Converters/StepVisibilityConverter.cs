using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ClippingSoftware.App.Converters;

/// <summary>
/// Shows an element only while the bound step index equals the ConverterParameter, collapsing it
/// otherwise - how the setup wizard puts all four steps in one Grid and reveals one at a time.
///
/// A converter rather than a DataTrigger per panel because the parameter form keeps the step number at
/// the usage site in the XAML (`Visibility="{Binding CurrentStep, Converter=..., ConverterParameter=2}"`),
/// so adding or reordering a step is a one-number edit rather than a new Style block.
/// </summary>
public class StepVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var current = value as int? ?? -1;
        var target = parameter switch
        {
            int i => i,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => -1,
        };

        return current == target ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("StepVisibilityConverter is display-only.");
}
