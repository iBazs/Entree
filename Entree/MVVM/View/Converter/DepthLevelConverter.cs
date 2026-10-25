// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Windows.Data;

namespace Entree.MVVM.View.Converter;

public class DepthLevelConverter : IValueConverter
{
   public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value switch
            {
                     >= 2 and var levels => $"{levels} levels deep",
                     1                   => "Top folder only",
                     int                 => "All subfolders",
                     _                   => string.Empty
            };

   public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Binding.DoNothing;
}
