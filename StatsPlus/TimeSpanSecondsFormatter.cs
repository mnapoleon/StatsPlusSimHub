using System;
using System.Globalization;
using System.Windows.Data;

namespace StatsPlus
{
    public class TimeSpanSecondsFormatter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return string.Empty;
            }

            double seconds;
            switch (value)
            {
                case double doubleValue:
                    seconds = doubleValue;
                    break;
                case float floatValue:
                    seconds = floatValue;
                    break;
                case int intValue:
                    seconds = intValue;
                    break;
                default:
                    return value.ToString();
            }

            if (seconds <= 0)
            {
                return "00:00.000";
            }

            TimeSpan time = TimeSpan.FromSeconds(seconds);
            int minutes = (int)time.TotalMinutes;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}.{2:000}", minutes, time.Seconds, time.Milliseconds);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
