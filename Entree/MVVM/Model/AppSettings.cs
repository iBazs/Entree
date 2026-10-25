// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using System.Text.Json.Serialization;
using Config.Net;
using Serilog.Events;
using Entree.Core.Helpers;

namespace Entree.MVVM.Model;

public interface IAppSettings
{
   [Option(Alias = "AppLogPath")] string AppLogPath { get; set; }

   [Option(Alias = "AppResultPath")] string AppResultPath { get; set; }

   [Option(Alias = "ThreadLimit", DefaultValue = -1)]
   int ThreadLimit { get; set; }

   [Option(Alias = "Log", DefaultValue = LogEventLevel.Information)]
   LogEventLevel LogLevel { get; }

   [Option(Alias = "Theme", DefaultValue = AppTheme.System)]
   string Theme { get; set; }
}

public class AppSettings : IAppSettings
{
   public string AppLogPath { get; set; } = Path.Combine(
                                                         Environment.GetFolderPath(Environment.SpecialFolder
                                                                        .LocalApplicationData),
                                                         "Entree",
                                                         "Log"
                                                        );

   public string AppResultPath { get; set; } = Path.Combine(
                                                            Environment.GetFolderPath(Environment.SpecialFolder
                                                                           .MyDocuments),
                                                            "Entree",
                                                            "Results"
                                                           );

   public              int           ThreadLimit { get; set; } = (int)(Environment.ProcessorCount * 1.5);
   [JsonIgnore] public LogEventLevel LogLevel    => LogEventLevel.Information;
   public              string        Theme       { get; set; } = AppTheme.System;
}