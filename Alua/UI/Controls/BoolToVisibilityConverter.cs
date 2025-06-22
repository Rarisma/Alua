using AutoMapper;
using Microsoft.UI.Xaml.Data;

namespace Alua.UI.Controls;

public class BoolToVisibilityConverter : IValueConverter, IValueConverter<bool, Visibility>
{
    // XAML binding: non‑generic interface
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool flag)
            return flag ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return (value is Visibility visibility && visibility == Visibility.Visible);
    }

    // AutoMapper conversion: generic interface
    public Visibility Convert(bool sourceMember, ResolutionContext context)
    {
        return sourceMember ? Visibility.Visible : Visibility.Collapsed;
    }
}