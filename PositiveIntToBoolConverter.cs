using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace GpsSync;

public class PositiveIntToBoolConverter : IValueConverter
{
	public static readonly PositiveIntToBoolConverter Instance = new PositiveIntToBoolConverter();

	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return value is int num && num > 0;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}
