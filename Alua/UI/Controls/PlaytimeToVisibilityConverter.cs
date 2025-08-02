using Microsoft.UI.Xaml.Data;

namespace Alua.UI.Controls;

public class PlaytimeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int playtimeMinutes)
            return playtimeMinutes >= 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}