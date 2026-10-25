// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Windows.Data;
using Serilog.Events;

namespace Entree.MVVM.View.Converter;

public class LogEventRenderConverter : IValueConverter
{
   public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is LogEvent logEvent ? Format(logEvent) : value?.ToString() ?? string.Empty;

   public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Binding.DoNothing;

   public static string Format(LogEvent logEvent) =>
            $"{logEvent.Timestamp:HH:mm:ss}  {Level(logEvent.Level)}  {logEvent.RenderMessage()}" +
            (logEvent.Exception is { } ex ? $"  ({ex.Message})" : string.Empty);

   private static string Level(LogEventLevel level) =>
            level switch
            {
                     LogEventLevel.Verbose     => "VRB",
                     LogEventLevel.Debug       => "DBG",
                     LogEventLevel.Information => "INF",
                     LogEventLevel.Warning     => "WRN",
                     LogEventLevel.Error       => "ERR",
                     _                         => "FTL"
            };
}
