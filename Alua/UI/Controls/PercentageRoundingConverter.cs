using Microsoft.UI.Xaml.Data;

namespace Alua.UI.Controls;

public class PercentageRoundingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double percentage)
        {
            return Math.Round(percentage, 2).ToString("F2");
        }
        
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
