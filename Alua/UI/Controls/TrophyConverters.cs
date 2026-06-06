using Alua.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Alua.UI.Controls;

/// <summary>
/// Maps a <see cref="TrophyKind"/> to a tier-coloured brush for the PlayStation trophy badge.
/// Uses generic metallic tier colours (not platform artwork). <see cref="TrophyKind.None"/> is
/// transparent so non-PSN achievements show nothing.
/// </summary>
public sealed class TrophyTypeToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Bronze   = new(Color.FromArgb(0xFF, 0xCD, 0x7F, 0x32));
    private static readonly SolidColorBrush Silver   = new(Color.FromArgb(0xFF, 0xC0, 0xC0, 0xC0));
    private static readonly SolidColorBrush Gold     = new(Color.FromArgb(0xFF, 0xFF, 0xD7, 0x00));
    private static readonly SolidColorBrush Platinum = new(Color.FromArgb(0xFF, 0x8F, 0xB8, 0xC9));
    private static readonly SolidColorBrush None     = new(Colors.Transparent);

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is TrophyKind kind
            ? kind switch
            {
                TrophyKind.Bronze   => Bronze,
                TrophyKind.Silver   => Silver,
                TrophyKind.Gold     => Gold,
                TrophyKind.Platinum => Platinum,
                _                   => None
            }
            : None;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
