// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32.TaskScheduler;
using Serilog;
using Entree.Core.BaseClass;
using Entree.MVVM.Model;
using Entree.MVVM.ViewModel;
using ScheduledTask = Microsoft.Win32.TaskScheduler.Task;
using Task = System.Threading.Tasks.Task;
using Trigger = Microsoft.Win32.TaskScheduler.Trigger;

namespace Entree.Services;

public interface ITaskSchedulerService
{
   Task<List<JobViewModel>> LoadJobsAsync();
   Task<bool>               SaveJobAsync(JobViewModel job, TaskScheduleSettings settings);
   Task<bool>               DeleteJobAsync(string taskName);
   Task<bool>               DeleteAllJobsAsync();
   Task                     EnsureTaskFolderExistsAsync();
}

public record TaskScheduleSettings(
         DateTime ScheduleDate,
         TimeSpan ScheduleTime,
         ScheduleType ScheduleType,
         int RecurrenceInterval,
         bool SynchronizeTimeZones,
         DaysOfTheWeek? SelectedDaysOfWeek,
         int MonthlyOptionType,
         int DayOfMonth,
         int[] SelectedDays,
         MonthsOfTheYear SelectedMonths,
         int MonthWeekNumber,
         int MonthDayOfWeek
);

public class TaskSchedulerService : ITaskSchedulerService
{
   private static readonly string TaskFolderName = JobViewModel.TaskFolderName;

   public async Task<List<JobViewModel>> LoadJobsAsync()
   {
      var jobs = new List<JobViewModel>();

      try
      {
         await Task.Run(() =>
         {
            using var ts = new TaskService();
            var exeFileName = Path.GetFileName(Process.GetCurrentProcess().MainModule?.FileName ??
                                               Application.ResourceAssembly.Location);

            try
            {
               var searchTasks = GetTaskFolder(ts).AllTasks.Where(x =>
               {
                  if (!string.IsNullOrEmpty(x.Definition.RegistrationInfo.Author) &&
                      x.Definition.RegistrationInfo.Author.Contains(TaskFolderName)) return true;
                  return x.Definition.Actions.Count != 0 &&
                         x.Definition.Actions.OfType<ExecAction>()
                          .Any(a => a.Path.Contains(exeFileName));
               });

               jobs.AddRange(from task in searchTasks
                             where !IsTaskSchedulerV1() ||
                                   (task.Definition.RegistrationInfo.Description?.Contains(TaskFolderName) ?? false)
                             select CreateJobViewModelFromTask(task));
            }
            catch (Exception ex)
            {
               Log.Warning(ex, "Error loading individual tasks");
            }
         });
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Error loading tasks: Could not get loaded the current tasks.");
      }

      return jobs;
   }

   public async Task<bool> SaveJobAsync(JobViewModel job, TaskScheduleSettings settings)
   {
      try
      {
         return await Task.Run(() =>
         {
            Log.Information("Saving task: {TaskName}, Schedule: {ScheduleType}",
                            job.Name, settings.ScheduleType);

            using var ts     = new TaskService();
            var       td     = ts.NewTask();
            var       folder = GetTaskFolder(ts);

            ConfigureTaskDefinition(td, job);
            CreateTriggers(td, settings);

            folder.RegisterTaskDefinition(job.Name, td, TaskCreation.CreateOrUpdate, null, null,
                                          TaskLogonType.InteractiveToken);
            Log.Information("Task '{Name}' saved", job.Name);

            var updatedTask = folder.Tasks.FirstOrDefault(t => t.Name == job.Name);
            if (updatedTask == null) return true;
            job.NextRunTime = NextRun(updatedTask);
            job.CommandLine = BuildCommandLineArguments(job);

            return true;
         });
      }
      catch (UnauthorizedAccessException ex) when (job.RunElevated)
      {
         Log.Error(ex, "Saving elevated task '{TaskName}' requires Entrée to run as administrator.", job.Name);
         return false;
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Error saving task '{TaskName}': {Error}", job.Name, ex.Message);
         return false;
      }
   }

   public async Task<bool> DeleteJobAsync(string taskName)
   {
      try
      {
         return await Task.Run(() =>
         {
            using var ts     = new TaskService();
            var       folder = GetTaskFolder(ts);
            folder.DeleteTask(taskName, false);
            return true;
         });
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Could not delete the task({Name}). {Message}", taskName, ex.Message);
         return false;
      }
   }

   public async Task<bool> DeleteAllJobsAsync()
   {
      var deleted = true;
      foreach (var job in await LoadJobsAsync())
         deleted &= await DeleteJobAsync(job.Name);
      if (!deleted) return false;

      await Task.Run(() =>
      {
         try
         {
            using var ts = new TaskService();
            if (!IsTaskSchedulerV1(ts) && ts.GetFolder(TaskFolderName) is not null)
               ts.RootFolder.DeleteFolder(TaskFolderName, false);
         }
         catch (Exception ex)
         {
            Log.Warning(ex, "Could not delete the task folder {TaskFolderName}", TaskFolderName);
         }
      });
      return true;
   }

   public async Task EnsureTaskFolderExistsAsync()
   {
      await Task.Run(() =>
      {
         try
         {
            using var ts = new TaskService();
            if (IsTaskSchedulerV1())
            {
               Log.Debug("Task Scheduler 1.0 detected. Using root folder instead of custom folder.");
               return;
            }

            try
            {
               _ = ts.GetFolder(TaskFolderName) ?? ts.RootFolder.CreateFolder("\\" + TaskFolderName, "Entrée tasks");
               Log.Debug("Created task scheduler folder: {TaskFolderName}", TaskFolderName);
            }
            catch (DirectoryNotFoundException)
            {
            }
         }
         catch (Exception ex)
         {
            Log.Error(ex, "Error creating task folder.");
         }
      });
   }

   public static bool IsTaskSchedulerV1()
   {
      using var ts = new TaskService();
      return IsTaskSchedulerV1(ts);
   }

   private static bool IsTaskSchedulerV1(TaskService ts) => ts.HighestSupportedVersion < new Version(2, 0);

   private static TaskFolder GetTaskFolder(TaskService ts) =>
            IsTaskSchedulerV1(ts) ? ts.RootFolder : ts.GetFolder(TaskFolderName) ?? ts.RootFolder;

   private static DateTime? NextRun(ScheduledTask task) =>
            task.NextRunTime == DateTime.MinValue ? null : task.NextRunTime;

   private static JobViewModel CreateJobViewModelFromTask(ScheduledTask task)
   {
      var job = new Job
                {
                         Name           = task.Name,
                         Status         = task.State.ToString(),
                         SearchFilePath = GetSearchFilePath(task),
                         RunElevated    = task.Definition.Principal.RunLevel == TaskRunLevel.Highest,
                         IsRunning      = task.State == TaskState.Running,
                         NextRunTime    = NextRun(task),
                         ScheduleType   = DetermineScheduleType(task),
                         CommandLine    = GetCommandLine(task)
                };

      return new JobViewModel(job)
             {
                      Schedule = ExtractScheduleSettings(task.Definition.Triggers.Count > 0
                                                                  ? task.Definition.Triggers[0]
                                                                  : null)
             };
   }

   private static void ConfigureTaskDefinition(TaskDefinition td, JobViewModel job)
   {
      var descriptionPrefix = IsTaskSchedulerV1() ? TaskFolderName + ": " : "";
      td.RegistrationInfo.Author      = TaskFolderName;
      td.RegistrationInfo.Description = descriptionPrefix + "Scheduled search task";
      td.Principal.RunLevel           = job.RunElevated ? TaskRunLevel.Highest : TaskRunLevel.LUA;
      td.Principal.LogonType          = TaskLogonType.InteractiveToken;

      var executable = Process.GetCurrentProcess().MainModule?.FileName ?? Application.ResourceAssembly.Location;
      td.Actions.Add(new ExecAction(executable, BuildCommandLineArguments(job), Path.GetDirectoryName(executable)));
   }

   private static void CreateTriggers(TaskDefinition td, TaskScheduleSettings settings)
   {
      var localStart = DateTime.SpecifyKind(settings.ScheduleDate.Date.Add(settings.ScheduleTime), DateTimeKind.Unspecified);
      var startTime  = settings.SynchronizeTimeZones && settings.ScheduleType != ScheduleType.OneTime
                                ? localStart.ToUniversalTime()
                                : localStart;

      switch (settings.ScheduleType)
      {
         default:
         case ScheduleType.OneTime:
            td.Triggers.Add(new TimeTrigger { StartBoundary = startTime });
            Log.Debug("Created one-time trigger for {StartTime}", startTime);
            break;

         case ScheduleType.Daily:
            var dailyTrigger = new DailyTrigger
                               {
                                        StartBoundary = startTime,
                                        DaysInterval  = (short)settings.RecurrenceInterval
                               };
            td.Triggers.Add(dailyTrigger);
            Log.Debug("Created daily trigger: Interval={Interval}, Start={StartTime}",
                      settings.RecurrenceInterval, startTime);
            break;

         case ScheduleType.Weekly:
            if (settings.SelectedDaysOfWeek == null)
               throw new InvalidOperationException("Selected days of week is required for weekly schedule");

            var weeklyTrigger = new WeeklyTrigger
                                {
                                         StartBoundary = startTime,
                                         DaysOfWeek    = settings.SelectedDaysOfWeek.Value,
                                         WeeksInterval = (short)settings.RecurrenceInterval
                                };
            td.Triggers.Add(weeklyTrigger);
            Log.Debug("Created weekly trigger: Days={Days}, Interval={Interval}, Start={StartTime}",
                      weeklyTrigger.DaysOfWeek, settings.RecurrenceInterval, startTime);
            break;

         case ScheduleType.Monthly:
            CreateMonthlyTriggers(td, settings, startTime);
            break;
      }
   }

   private static void CreateMonthlyTriggers(TaskDefinition td, TaskScheduleSettings settings, DateTime startTime)
   {
      if (settings.MonthlyOptionType == 0)
      {
         var selectedDays = settings.SelectedDays.Length == 0
                  ? [settings.DayOfMonth]
                  : settings.SelectedDays;

         var monthlyTrigger = new MonthlyTrigger
                              {
                                       StartBoundary = startTime,
                                       MonthsOfYear  = settings.SelectedMonths,
                                       DaysOfMonth   = selectedDays
                              };
         td.Triggers.Add(monthlyTrigger);
         Log.Debug("Created monthly trigger for days {Days} of {Months}, starting {StartTime}",
                   string.Join(", ", selectedDays), monthlyTrigger.MonthsOfYear, startTime);
      }
      else
      {
         var monthlyDowTrigger = new MonthlyDOWTrigger
                                 {
                                          StartBoundary = startTime,
                                          MonthsOfYear  = settings.SelectedMonths,
                                          DaysOfWeek    = GetDayOfWeekFromIndex(settings.MonthDayOfWeek),
                                          WeeksOfMonth  = GetWhichWeekFromIndex(settings.MonthWeekNumber)
                                 };
         td.Triggers.Add(monthlyDowTrigger);
         Log.Debug("Created monthly trigger for {Week} {Day} of {Months}, starting {StartTime}",
                   GetWeekNumberName(settings.MonthWeekNumber),
                   GetDayOfWeekFromIndex(settings.MonthDayOfWeek),
                   monthlyDowTrigger.MonthsOfYear, startTime);
      }
   }


   private static string BuildCommandLineArguments(JobViewModel job) =>
            BuildCommandLineArguments(job.SearchFilePath);

   internal static string BuildCommandLineArguments(string? searchFilePath)
   {
      var args = new StringBuilder("--silent");
      if (!string.IsNullOrWhiteSpace(searchFilePath))
         args.Append(" --search \"").Append(searchFilePath).Append('"');
      return args.ToString();
   }

   private static ScheduleType DetermineScheduleType(ScheduledTask task)
   {
      if (task.Definition.Triggers.Count <= 0) return ScheduleType.OneTime;
      return task.Definition.Triggers[0] switch
             {
                      DailyTrigger      => ScheduleType.Daily,
                      WeeklyTrigger     => ScheduleType.Weekly,
                      MonthlyTrigger    => ScheduleType.Monthly,
                      MonthlyDOWTrigger => ScheduleType.Monthly,
                      _                 => ScheduleType.OneTime
             };
   }

   private static string GetCommandLine(ScheduledTask task) => task.Definition.Actions.Count > 0
            ? task.Definition.Actions.OfType<ExecAction>().First().Arguments
            : string.Empty;

   private static string GetSearchFilePath(ScheduledTask task)
   {
      if (task.Definition.Actions.Count == 0) return string.Empty;
      foreach (var action in task.Definition.Actions.OfType<ExecAction>())
      {
         var path = ExtractSearchArgument(action.Arguments);
         if (!string.IsNullOrEmpty(path)) return path;
      }

      return string.Empty;
   }

   internal static string ExtractSearchArgument(string? arguments)
   {
      if (string.IsNullOrEmpty(arguments)) return string.Empty;

      const string flag      = "--search";
      var          flagIndex = arguments.IndexOf(flag, StringComparison.Ordinal);
      if (flagIndex < 0) return string.Empty;

      var rest = arguments[(flagIndex + flag.Length)..].TrimStart();
      if (rest.Length == 0) return string.Empty;

      if (rest[0] is '"' or '\'')
      {
         var quote = rest[0];
         var end   = rest.IndexOf(quote, 1);
         return end < 0 ? rest[1..] : rest[1..end];
      }

      var space = rest.IndexOf(' ');
      return space < 0 ? rest : rest[..space];
   }

   private static string GetWeekNumberName(int weekNumber) => weekNumber switch
                                                              {
                                                                       1 => "Second",
                                                                       2 => "Third",
                                                                       3 => "Fourth",
                                                                       4 => "Last",
                                                                       _ => "First"
                                                              };

   internal static DaysOfTheWeek GetDayOfWeekFromIndex(int dayOfWeek) => dayOfWeek switch
                                                                         {
                                                                                  1 => DaysOfTheWeek.Tuesday,
                                                                                  2 => DaysOfTheWeek.Wednesday,
                                                                                  3 => DaysOfTheWeek.Thursday,
                                                                                  4 => DaysOfTheWeek.Friday,
                                                                                  5 => DaysOfTheWeek.Saturday,
                                                                                  6 => DaysOfTheWeek.Sunday,
                                                                                  _ => DaysOfTheWeek.Monday
                                                                         };

   internal static WhichWeek GetWhichWeekFromIndex(int weekNumber) => weekNumber switch
                                                                      {
                                                                               1 => WhichWeek.SecondWeek,
                                                                               2 => WhichWeek.ThirdWeek,
                                                                               3 => WhichWeek.FourthWeek,
                                                                               4 => WhichWeek.LastWeek,
                                                                               _ => WhichWeek.FirstWeek
                                                                      };

   internal static int GetIndexFromDayOfWeek(DaysOfTheWeek dayOfWeek) => dayOfWeek switch
                                                                         {
                                                                                  DaysOfTheWeek.Tuesday   => 1,
                                                                                  DaysOfTheWeek.Wednesday => 2,
                                                                                  DaysOfTheWeek.Thursday  => 3,
                                                                                  DaysOfTheWeek.Friday    => 4,
                                                                                  DaysOfTheWeek.Saturday  => 5,
                                                                                  DaysOfTheWeek.Sunday    => 6,
                                                                                  _                       => 0
                                                                         };

   internal static int GetIndexFromWhichWeek(WhichWeek whichWeek) => whichWeek switch
                                                                     {
                                                                              WhichWeek.SecondWeek => 1,
                                                                              WhichWeek.ThirdWeek  => 2,
                                                                              WhichWeek.FourthWeek => 3,
                                                                              WhichWeek.LastWeek   => 4,
                                                                              _                    => 0
                                                                     };

   internal static TaskScheduleSettings ExtractScheduleSettings(Trigger? trigger)
   {
      var boundary = trigger?.StartBoundary ?? DateTime.Now;
      var start    = boundary.Kind == DateTimeKind.Utc ? boundary.ToLocalTime() : boundary;
      var synced   = trigger is not (null or TimeTrigger) && boundary.Kind != DateTimeKind.Unspecified;

      return (trigger switch
             {
                      DailyTrigger d => BuildSettings(ScheduleType.Daily, start, Math.Max(1, (int)d.DaysInterval)),

                      WeeklyTrigger w => BuildSettings(ScheduleType.Weekly, start, Math.Max(1, (int)w.WeeksInterval),
                                                       selectedDaysOfWeek: w.DaysOfWeek),

                      MonthlyTrigger m => BuildSettings(ScheduleType.Monthly, start, 1,
                                                        selectedMonths: m.MonthsOfYear,
                                                        monthlyOptionType: 0,
                                                        selectedDays: m.DaysOfMonth ?? [],
                                                        dayOfMonth: m.DaysOfMonth is { Length: > 0 }
                                                                 ? m.DaysOfMonth[0]
                                                                 : start.Day),

                      MonthlyDOWTrigger md => BuildSettings(ScheduleType.Monthly, start, 1,
                                                            selectedMonths: md.MonthsOfYear,
                                                            monthlyOptionType: 1,
                                                            monthDayOfWeek: GetIndexFromDayOfWeek(md.DaysOfWeek),
                                                            monthWeekNumber: GetIndexFromWhichWeek(md.WeeksOfMonth)),

                      _ => BuildSettings(ScheduleType.OneTime, start, 1)
             }) with { SynchronizeTimeZones = synced };
   }

   private static TaskScheduleSettings BuildSettings(
            ScheduleType scheduleType, DateTime start, int recurrenceInterval,
            DaysOfTheWeek? selectedDaysOfWeek = null, int monthlyOptionType = 0, int dayOfMonth = 1,
            int[]? selectedDays = null, MonthsOfTheYear selectedMonths = 0, int monthWeekNumber = 0,
            int monthDayOfWeek = 0) =>
            new(start.Date, start.TimeOfDay, scheduleType, recurrenceInterval, false,
                selectedDaysOfWeek, monthlyOptionType, dayOfMonth, selectedDays ?? [], selectedMonths,
                monthWeekNumber, monthDayOfWeek);
}