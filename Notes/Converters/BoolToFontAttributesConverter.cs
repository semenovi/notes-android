using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace Notes.Converters
{
    public class BoolToFontAttributesConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSelected && isSelected)
                return FontAttributes.Bold;
            
            return FontAttributes.None;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is FontAttributes attributes && attributes == FontAttributes.Bold);
        }
    }
}