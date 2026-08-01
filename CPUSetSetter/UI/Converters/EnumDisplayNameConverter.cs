using System.Globalization;
using System.Text;
using System.Windows.Data;


namespace CPUSetSetter.UI.Converters
{
    /// <summary>
    /// Convert an enum value (or null) to a friendly display name, e.g. "BelowNormal" -> "Below Normal", null -> "Default"
    /// </summary>
    public class EnumDisplayNameConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is null)
                return "Default";

            string name = value.ToString() ?? "";
            if (name == "NotSet")
                return "Default";

            StringBuilder displayName = new();
            for (int i = 0; i < name.Length; ++i)
            {
                if (i > 0 && char.IsUpper(name[i]) && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])))
                    displayName.Append(' ');
                displayName.Append(name[i]);
            }
            return displayName.ToString();
        }

        public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}