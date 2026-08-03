using System.Globalization;
using System.Windows.Data;


namespace CPUSetSetter.UI.Converters
{
    /// <summary>
    /// Converts a fraction (0-1) to a pixel width for a fixed-width bar track. The track width is
    /// passed as the converter parameter (e.g. ConverterParameter=90)
    /// </summary>
    public class PercentToWidthConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            double fraction = value is double d ? d : 0;
            double trackWidth = double.TryParse(parameter?.ToString(), out double w) ? w : 0;
            return Math.Max(0, Math.Min(trackWidth, fraction * trackWidth));
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
