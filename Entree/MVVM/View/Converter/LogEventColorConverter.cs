// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Serilog.Events;

namespace Entree.MVVM.View.Converter;

public class LogEventColorConverter : IValueConverter
{
   public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Application.Current?.TryFindResource(value switch
                                                 {
                                                          LogEventLevel.Fatal or LogEventLevel.Error => "SystemFillColorCriticalBrush",
                                                          LogEventLevel.Warning => "SystemFillColorCautionBrush",
                                                          LogEventLevel.Debug or LogEventLevel.Verbose => "TextFillColorSecondaryBrush",
                                                          _ => "TextFillColorPrimaryBrush"
                                                 });

   public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Binding.DoNothing;
}
