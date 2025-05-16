namespace Notes.Helpers;

using System.Globalization;

public class BoolToColorConverter : IValueConverter
{
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
  {
    if (value is bool isSelected && isSelected)
      return Colors.LightSkyBlue;
    return Colors.Transparent;
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
  {
    throw new NotImplementedException();
  }
}