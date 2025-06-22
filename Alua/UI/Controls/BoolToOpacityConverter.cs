using AutoMapper;
using Microsoft.UI.Xaml.Data;

namespace Alua.UI.Controls;

public class BoolToOpacityConverter : IValueConverter, IValueConverter<bool, double>
{
    // XAML binding: non‑generic interface
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool flag)
            return flag ? 1.0 : 0.5;
        return 0.5;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if(value is double opacity)
            return opacity == 1.0;
        return false;
    }

    // AutoMapper conversion: generic interface
    public double Convert(bool sourceMember, ResolutionContext context)
    {
        return sourceMember ? 1.0 : 0.5;
    }
}
