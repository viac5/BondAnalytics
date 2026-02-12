using System;
using System.Globalization;
using System.Windows.Data;

namespace App.Converters
{
    public class HeightScaleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal dec)
                return (double)dec / 10; // масштабирование

            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
