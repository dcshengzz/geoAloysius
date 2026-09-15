using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace GpsSync;

/// <summary>Returns White when this chip's index matches FilterMode, else Transparent.</summary>
public class FilterChipBgConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool selected = value is int i && parameter is string s && int.TryParse(s, out int p) && i == p;
        return selected ? Colors.White : Colors.Transparent;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Returns dark accent when selected, white when unselected.</summary>
public class FilterChipTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool selected = value is int i && parameter is string s && int.TryParse(s, out int p) && i == p;
        return selected ? Color.FromArgb("#1a3a5c") : Colors.White;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
