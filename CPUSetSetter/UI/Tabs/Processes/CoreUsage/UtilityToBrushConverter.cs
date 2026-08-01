using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;


namespace CPUSetSetter.UI.Tabs.Processes.CoreUsage
{
    /// <summary>
    /// Maps a core usage value (0..1) to a heat color: gray at idle, rising through green/yellow/orange to red
    /// </summary>
    public class UtilityToBrushConverter : IValueConverter
    {
        private static readonly (double Stop, Color Color)[] Gradient =
        [
            (0.00, Color.FromRgb(0xE0, 0xE0, 0xE0)),
            (0.25, Color.FromRgb(0x66, 0xBB, 0x6A)),
            (0.50, Color.FromRgb(0xFF, 0xEE, 0x58)),
            (0.75, Color.FromRgb(0xFF, 0x98, 0x00)),
            (1.00, Color.FromRgb(0xE5, 0x39, 0x35)),
        ];

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double usage = value is double d ? d : 0.0;
            usage = Math.Clamp(usage, 0.0, 1.0);
            return new SolidColorBrush(GetColor(usage));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static Color GetColor(double t)
        {
            for (int i = 0; i < Gradient.Length - 1; ++i)
            {
                (double Stop, Color Color) from = Gradient[i];
                (double Stop, Color Color) to = Gradient[i + 1];
                if (t <= to.Stop)
                {
                    double local = to.Stop == from.Stop ? 0 : (t - from.Stop) / (to.Stop - from.Stop);
                    return Lerp(from.Color, to.Color, local);
                }
            }
            return Gradient[^1].Color;
        }

        private static Color Lerp(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (byte)(a.A + (b.A - a.A) * t),
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }
    }
}
