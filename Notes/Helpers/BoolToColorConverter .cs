namespace Notes.Helpers;

using System.Globalization;

public class BoolToColorConverter : IValueConverter
{
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
  {
    if (value is bool isSelected && isSelected)
    {
      return parameter?.ToString() == "folder"
          ? Color.FromArgb("#D4D4D4")
          : Color.FromArgb("#FFE580");
    }
    return Colors.Transparent;
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
  {
    throw new NotImplementedException();
  }
}