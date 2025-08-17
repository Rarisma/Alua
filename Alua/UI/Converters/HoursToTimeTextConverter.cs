using Microsoft.UI.Xaml.Data;
using System;

namespace Alua.UI.Converters;

public class HoursToTimeTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double hours && hours > 0)
        {
            if (hours < 1)
            {
                int minutes = (int)(hours * 60);
                return $"{minutes}m";
            }
            
            int wholeHours = (int)hours;
            int remainingMinutes = (int)((hours - wholeHours) * 60);
            
            if (remainingMinutes > 0)
                return $"{wholeHours}h {remainingMinutes}m";
            
            return $"{wholeHours}h";
        }
        
        return "N/A";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}