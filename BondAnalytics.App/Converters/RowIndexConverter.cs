using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace BondAnalytics.App.Converters
{
    public class RowIndexConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var row = values[0] as DataGridRow;
            if (row == null)
                return "";

            return (row.GetIndex() + 1).ToString();
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
