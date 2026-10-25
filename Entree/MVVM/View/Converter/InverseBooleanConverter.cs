// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Windows.Data;

namespace Entree.MVVM.View.Converter;

public class InverseBooleanConverter : IValueConverter
{
   public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

   public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
