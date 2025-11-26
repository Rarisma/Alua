using AutoMapper;
using Microsoft.UI.Xaml.Data;
//AND YOU GET A BOOL CONVERTER AND YOU GET A ...
namespace Alua.UI.Controls;

/// <summary>
/// Converts a boolean to Visibility, inverting the logic (false = Visible, true = Collapsed)
/// </summary>
public class BoolNegationToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Collapsed;
        }
        return false;
    }
}

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

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Visible;
        }
        return false;
    }
}
