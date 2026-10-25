// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Windows.Data;

namespace Entree.MVVM.View.Converter;

public class BoolToTextConverter : IValueConverter
{
   public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            (parameter as string ?? "On|Off").Split('|') is [var whenTrue, var whenFalse]
                     ? value is true ? whenTrue : whenFalse
                     : string.Empty;

   public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Binding.DoNothing;
}
