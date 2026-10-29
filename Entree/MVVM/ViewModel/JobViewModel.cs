// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32.TaskScheduler;
using Serilog;
using Wpf.Ui.Controls;
using Entree.Core.BaseClass;
using Entree.MVVM.Model;
using Entree.Services;

namespace Entree.MVVM.ViewModel;

public class JobViewModel : Core.BaseClass.ViewModel
{
   public static string TaskFolderName => "Entrée";

   public string Name
   {
      get => Job.Name;
      set
      {
         if (Job.Name == value) return;
         Job.Name = value;
         OnPropertyChanged();
      }
   }

   public string Status
   {
      get => Job.Status;
      private set
      {
         if (Job.Status == value) return;
         Job.Status = value;
         OnPropertyChanged();

         OnPropertyChanged(nameof(StatusText));
         OnPropertyChanged(nameof(StatusColor));
         OnPropertyChanged(nameof(StatusIcon));
         OnPropertyChanged(nameof(ProgressIcon));
         OnPropertyChanged(nameof(ProgressAppearance));
         OnPropertyChanged(nameof(ProgressVisibility));
      }
   }

   public string? SearchFilePath
   {
      get => Job.SearchFilePath;
      set
      {
         if (Job.SearchFilePath == value) return;
         Job.SearchFilePath = value;
         OnPropertyChanged();
      }
   }

   public bool IsRunning
   {
      get => Job.IsRunning;
      private set
      {
         if (Job.IsRunning == value) return;
         Job.IsRunning = value;
         OnPropertyChanged();

         OnPropertyChanged(nameof(StatusText));
         OnPropertyChanged(nameof(StatusColor));
         OnPropertyChanged(nameof(StatusIcon));
         OnPropertyChanged(nameof(ProgressIcon));
         OnPropertyChanged(nameof(ProgressAppearance));
         OnPropertyChanged(nameof(ProgressVisibility));
      }
   }

   public DateTime? NextRunTime
   {
      get => Job.NextRunTime;
      set
      {
         if (Job.NextRunTime == value) return;
         Job.NextRunTime = value;
         OnPropertyChanged();
         OnPropertyChanged(nameof(NextRunTimeVisibility));
         OnPropertyChanged(nameof(NoNextRunTimeVisibility));
      }
   }

   public bool RunElevated
   {
      get => Job.RunElevated;
      set
      {
         if (Job.RunElevated == value) return;
         Job.RunElevated = value;
         OnPropertyChanged();
      }
   }

   public ScheduleType ScheduleType
   {
      get => Job.ScheduleType;
      set
      {
         if (Job.ScheduleType == value) return;
         Job.ScheduleType = value;
         OnPropertyChanged();
         OnPropertyChanged(nameof(ScheduleTypeText));
      }
   }

   public string ScheduleTypeText => ScheduleType == ScheduleType.OneTime ? "One time" : ScheduleType.ToString();

   public string CommandLine
   {
      get => Job.CommandLine;
      set
      {
         if (Job.CommandLine == value) return;
         Job.CommandLine = value;
         OnPropertyChanged();
      }
   }

   public TaskScheduleSettings? Schedule { get; set; }

   private Job Job { get; }

   // ReSharper disable once UnusedMember.Global
   public JobViewModel(Job? job)
   {
      Job = job ?? new Job();

      RunNowCommand = new RelayCommand(Run);
      OpenSearchFileCommand =
               new RelayCommand(_ => OpenSearchFile(),
                                _ => !string.IsNullOrEmpty(SearchFilePath));
      // ReSharper restore AsyncVoidLambda
   }

   public RelayCommand OpenSearchFileCommand { get; }
   public RelayCommand RunNowCommand         { get; }

   private static readonly TimeSpan ClickCooldown = TimeSpan.FromSeconds(1);
   private static readonly TimeSpan PollInterval  = TimeSpan.FromSeconds(2);

   private DateTime         _lastControl = DateTime.MinValue;
   private DispatcherTimer? _poll;

   private void Run(object obj)
   {
      var now = DateTime.Now;
      if (now - _lastControl < ClickCooldown) return;
      _lastControl = now;
      if (IsRunning) RunStop();
      else RunNow();
   }

   private void RunNow()
   {
      if (!Control(task => task.Run(), true, "Running", "start")) return;
      _poll?.Stop();
      _poll = new DispatcherTimer(PollInterval, DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher.CurrentDispatcher);
      _poll.Start();
   }

   private void RunStop()
   {
      _poll?.Stop();
      Control(task => task.Stop(), false, "Ready", "stop");
   }

   private void Refresh()
   {
      try
      {
         using var ts   = new TaskService();
         var       task = FindTask(ts);
         if (task?.State == TaskState.Running) return;
         IsRunning = false;
         if (task is null) return;
         Status      = task.State.ToString();
         NextRunTime = task.NextRunTime == DateTime.MinValue ? null : task.NextRunTime;
      }
      catch (Exception ex)
      {
         IsRunning = false;
         Log.Warning("Could not read the state of task {Name}: {Message}", Job.Name, ex.Message);
      }

      _poll?.Stop();
   }

   private bool Control(Action<Microsoft.Win32.TaskScheduler.Task> action, bool isRunning, string status, string verb)
   {
      using var ts = new TaskService();
      try
      {
         if (FindTask(ts) is not { } task)
         {
            Log.Warning("Task {Name} was not found in Task Scheduler", Job.Name);
            return false;
         }

         action(task);
         IsRunning = isRunning;
         Status    = status;
         Log.Information("Task {Verb}: {Name}", verb, Job.Name);
         return true;
      }
      catch (Exception ex)
      {
         Log.Error("Failed to {Verb} the task: {Name}. {ex}", verb, Job.Name, ex.Message);
         return false;
      }
   }

   private Microsoft.Win32.TaskScheduler.Task? FindTask(TaskService ts) =>
            EntreeFolder(ts)?.Tasks.FirstOrDefault(t => t.Name == Job.Name) ??
            ts.RootFolder.Tasks.FirstOrDefault(t => t.Name == Job.Name);

   private static TaskFolder? EntreeFolder(TaskService ts)
   {
      if (TaskSchedulerService.IsTaskSchedulerV1()) return null;
      try
      {
         return ts.GetFolder("\\" + TaskFolderName);
      }
      catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
      {
         return null;
      }
   }

   private void OpenSearchFile()
   {
      Process.Start("explorer.exe",
                    $"/select,\"{Job.SearchFilePath}\"");
   }

   public string StatusText => Job.IsRunning ? "Running" : Job.Status;

   public Brush StatusColor => Job.Status.ToLowerInvariant() switch
                               {
                                        "running"  => StatusBrushes.Success,
                                        "ready"    => StatusBrushes.Info,
                                        "disabled" => StatusBrushes.Neutral,
                                        "error"    => StatusBrushes.Danger,
                                        "new"      => StatusBrushes.Accent,
                                        _          => StatusBrushes.Caution
                               };

   public SymbolRegular StatusIcon
   {
      get
      {
         return Job.Status.ToLower() switch
                {
                         "running"  => SymbolRegular.Play24,
                         "ready"    => SymbolRegular.CheckmarkCircle24,
                         "disabled" => SymbolRegular.Prohibited24,
                         "error"    => SymbolRegular.ErrorCircle24,
                         "new"      => SymbolRegular.Add24,
                         _          => SymbolRegular.QuestionCircle24
                };
      }
   }

   public SymbolRegular ProgressIcon
   {
      get
      {
         return Job.Status.ToLower() switch
                {
                         "running"  => SymbolRegular.Stop24,
                         "disabled" => SymbolRegular.Prohibited24,
                         _          => SymbolRegular.Play24
                };
      }
   }

   public ControlAppearance ProgressAppearance
   {
      get
      {
         return ProgressIcon switch
                {
                         SymbolRegular.Play24 => ControlAppearance.Success,
                         SymbolRegular.Stop24 => ControlAppearance.Danger,
                         _                    => ControlAppearance.Secondary
                };
      }
   }

   public Visibility ProgressVisibility => Job.IsRunning ? Visibility.Visible : Visibility.Collapsed;

   public Visibility NextRunTimeVisibility => Job.NextRunTime.HasValue ? Visibility.Visible : Visibility.Collapsed;

   public Visibility NoNextRunTimeVisibility => Job.NextRunTime.HasValue ? Visibility.Collapsed : Visibility.Visible;
}