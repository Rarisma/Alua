using Alua.Services.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Alua.UI.Controls;

/// <summary>
/// Visible when the bound enum value's name equals the ConverterParameter string
/// (e.g. bind a <see cref="CardProgressStyle"/> with parameter "Bar"). Used to show/hide
/// card elements per the selected appearance enum.
/// </summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value != null && parameter is string name &&
        string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Maps <see cref="CardTextAlignment"/> to a <see cref="TextAlignment"/>.</summary>
public sealed class CardTextAlignmentToTextAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is CardTextAlignment a
            ? a switch
            {
                CardTextAlignment.Left   => TextAlignment.Left,
                CardTextAlignment.Center => TextAlignment.Center,
                _                        => TextAlignment.Right
            }
            : TextAlignment.Right;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Maps <see cref="CardTextAlignment"/> to a <see cref="HorizontalAlignment"/> for the text block.</summary>
public sealed class CardTextAlignmentToHorizontalAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is CardTextAlignment a
            ? a switch
            {
                CardTextAlignment.Left   => HorizontalAlignment.Left,
                CardTextAlignment.Center => HorizontalAlignment.Center,
                _                        => HorizontalAlignment.Right
            }
            : HorizontalAlignment.Right;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Compact flag to card padding (true = denser).</summary>
public sealed class CompactToPaddingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? new Thickness(8) : new Thickness(12);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
