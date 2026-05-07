using System.Globalization;
using System.Windows.Data;

namespace TitanAILivePC.Core;

public sealed class MeterValueToPixelConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 ||
            values[0] is not double meterWidth ||
            values[1] is not double value)
        {
            return 0d;
        }

        var max = 100d;
        var normalized = Math.Clamp(value / max, 0, 1);
        var pixels = meterWidth * normalized;

        if (parameter is string mode && mode.Equals("left", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Clamp(pixels - 1, 0, meterWidth);
        }

        return Math.Clamp(pixels, 0, meterWidth);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
