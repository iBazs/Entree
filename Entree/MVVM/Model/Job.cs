// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using Entree.Core.BaseClass;

namespace Entree.MVVM.Model;

public class Job
{
   public string Name { get; set; } = "Search Task";

   public string Status { get; set; } = "Ready";

   public string? SearchFilePath { get; set; }

   public bool RunElevated { get; set; }

   public bool IsRunning { get; set; }

   public DateTime? NextRunTime { get; set; }

   public ScheduleType ScheduleType { get; set; } = ScheduleType.OneTime;

   public string CommandLine { get; set; } = string.Empty;
}