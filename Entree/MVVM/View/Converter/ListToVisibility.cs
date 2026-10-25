// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Entree.MVVM.View.Converter;

internal class ListToVisibilityConverter : IValueConverter
{
   public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is not int count ? Visibility.Collapsed :
            parameter is "invert"  ? count == 0 ? Visibility.Visible : Visibility.Collapsed :
            count > 0              ? Visibility.Visible : Visibility.Collapsed;

   public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is Visibility.Visible ? 1 : 0;
}