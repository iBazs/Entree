// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Entree.MVVM.View.Converter;

public class InverseBooleanToVisibilityConverter : IValueConverter
{
   public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is false ? Visibility.Visible : Visibility.Collapsed;

   public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is Visibility and not Visibility.Visible;
}