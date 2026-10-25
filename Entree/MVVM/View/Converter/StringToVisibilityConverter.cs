// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace Entree.MVVM.View.Converter;

public class StringToVisibilityConverter : IValueConverter
{
   public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            (value is string stringValue && string.IsNullOrEmpty(stringValue)) || value is SymbolRegular and 0 ||
            value is null
                     ? Visibility.Collapsed
                     : Visibility.Visible;

   public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is Visibility.Visible ? "string" : null;
}