using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;


namespace CPUSetSetter.UI.Tabs.Processes.CoreUsage
{
    /// <summary>
    /// Maps a core usage value (0..1) to a calm, muted color that only turns red once usage is high,
    /// avoiding the rainbow of vibrant heat colors that made the cells look loud
    /// </summary>
    public class UtilityToBrushConverter : IValueConverter
    {
        private const double HighThreshold = 0.80;

        // Muted slate for anything below the threshold, so cells stay calm; red only appears as usage rises high
        private static readonly Color Muted = Color.FromRgb(0x33, 0x36, 0x3A);
        private static readonly Color Hot = Color.FromRgb(0xD9, 0x2B, 0x2B);

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
            if (t <= HighThreshold)
                return Muted;

            double ratio = Math.Clamp((t - HighThreshold) / (1.0 - HighThreshold), 0.0, 1.0);
            return Lerp(Muted, Hot, ratio);
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
