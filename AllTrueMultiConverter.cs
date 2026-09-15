using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace GpsSync;

/// <summary>Returns true only when ALL bound bool values are true.</summary>
public class AllTrueMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        foreach (var v in values)
            if (v is not bool b || !b) return false;
        return true;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
