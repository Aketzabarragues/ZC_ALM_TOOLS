using System;
using System.Globalization;
using System.Windows.Data;

namespace ZC_ALM_TOOLS.Core
{
    public class ContainsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return false;

            string text = value.ToString().ToLower();
            string searchTerm = parameter.ToString().ToLower();

            return text.Contains(searchTerm);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}