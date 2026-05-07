using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TitanAILivePC.Core;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var invert = Equals(parameter, "Invert");
        var on = value is true;
        if (invert)
        {
            on = !on;
        }

        return on ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
