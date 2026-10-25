// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.Win32.TaskScheduler;
using Entree.Core.BaseClass;
using Entree.Services;
using Xunit;

namespace Entree.Tests;

public class TaskSchedulerServiceTests
{
   [Theory]
   [InlineData(@"C:\My Configs\acl.entree")]
   [InlineData(@"C:\acl.entree")]
   [InlineData(@"D:\a b\c d\nested file.entree")]
   [InlineData(@"\\server\share\team config.entree")]
   public void SearchFilePath_RoundTrips_EvenWithSpaces(string path)
   {
      var args   = TaskSchedulerService.BuildCommandLineArguments(path);
      var parsed = TaskSchedulerService.ExtractSearchArgument(args);

      Assert.Equal(path, parsed);
   }

   [Fact]
   public void BuildCommandLineArguments_NoPath_EmitsOnlySilent()
   {
      Assert.Equal("--silent", TaskSchedulerService.BuildCommandLineArguments(null));
      Assert.Equal("--silent", TaskSchedulerService.BuildCommandLineArguments("   "));
   }

   [Fact]
   public void ExtractSearchArgument_WithoutSearchFlag_ReturnsEmpty()
   {
      Assert.Equal(string.Empty, TaskSchedulerService.ExtractSearchArgument("--silent"));
      Assert.Equal(string.Empty, TaskSchedulerService.ExtractSearchArgument(""));
      Assert.Equal(string.Empty, TaskSchedulerService.ExtractSearchArgument(null));
   }

   [Fact]
   public void ExtractSearchArgument_UnquotedValue_ReadsUpToSpace()
   {
      Assert.Equal(@"C:\file.entree",
                   TaskSchedulerService.ExtractSearchArgument(@"--silent --search C:\file.entree"));
   }

   [Fact]
   public void ExtractScheduleSettings_Weekly_RestoresDaysIntervalAndTime()
   {
      var start = new DateTime(2026, 6, 28, 9, 30, 0);
      var trigger = new WeeklyTrigger
                    {
                             StartBoundary = start,
                             DaysOfWeek    = DaysOfTheWeek.Monday | DaysOfTheWeek.Wednesday | DaysOfTheWeek.Friday,
                             WeeksInterval = 2
                    };

      var settings = TaskSchedulerService.ExtractScheduleSettings(trigger);

      Assert.Equal(ScheduleType.Weekly, settings.ScheduleType);
      Assert.Equal(2, settings.RecurrenceInterval);
      Assert.Equal(DaysOfTheWeek.Monday | DaysOfTheWeek.Wednesday | DaysOfTheWeek.Friday,
                   settings.SelectedDaysOfWeek);
      Assert.Equal(start.Date, settings.ScheduleDate);
      Assert.Equal(start.TimeOfDay, settings.ScheduleTime);
   }

   [Fact]
   public void ExtractScheduleSettings_Daily_RestoresInterval()
   {
      var trigger = new DailyTrigger { StartBoundary = new DateTime(2026, 6, 28, 7, 0, 0), DaysInterval = 3 };

      var settings = TaskSchedulerService.ExtractScheduleSettings(trigger);

      Assert.Equal(ScheduleType.Daily, settings.ScheduleType);
      Assert.Equal(3, settings.RecurrenceInterval);
   }

   [Fact]
   public void ExtractScheduleSettings_MonthlyDayOfMonth_RestoresDaysAndMonths()
   {
      var trigger = new MonthlyTrigger
                    {
                             StartBoundary = new DateTime(2026, 1, 15, 6, 0, 0),
                             MonthsOfYear  = MonthsOfTheYear.January | MonthsOfTheYear.July,
                             DaysOfMonth   = [1, 15]
                    };

      var settings = TaskSchedulerService.ExtractScheduleSettings(trigger);

      Assert.Equal(ScheduleType.Monthly, settings.ScheduleType);
      Assert.Equal(0, settings.MonthlyOptionType);
      Assert.Equal([1, 15], settings.SelectedDays);
      Assert.Equal(MonthsOfTheYear.January | MonthsOfTheYear.July, settings.SelectedMonths);
   }

   [Fact]
   public void ExtractScheduleSettings_MonthlyDayOfWeek_RestoresWeekAndDayIndexes()
   {
      var trigger = new MonthlyDOWTrigger
                    {
                             StartBoundary = new DateTime(2026, 1, 1, 0, 0, 0),
                             MonthsOfYear  = MonthsOfTheYear.AllMonths,
                             DaysOfWeek    = DaysOfTheWeek.Wednesday,
                             WeeksOfMonth  = WhichWeek.ThirdWeek
                    };

      var settings = TaskSchedulerService.ExtractScheduleSettings(trigger);

      Assert.Equal(ScheduleType.Monthly, settings.ScheduleType);
      Assert.Equal(1, settings.MonthlyOptionType);
      Assert.Equal(2, settings.MonthDayOfWeek);
      Assert.Equal(2, settings.MonthWeekNumber);
   }

   [Fact]
   public void ExtractScheduleSettings_NullTrigger_DefaultsToOneTime()
   {
      var settings = TaskSchedulerService.ExtractScheduleSettings(null);
      Assert.Equal(ScheduleType.OneTime, settings.ScheduleType);
      Assert.False(settings.SynchronizeTimeZones);
   }

   [Fact]
   public void ExtractScheduleSettings_UtcRecurringStart_IsSynchronizedAndShownInLocalTime()
   {
      var local   = new DateTime(2026, 6, 28, 7, 0, 0, DateTimeKind.Local);
      var trigger = new DailyTrigger { StartBoundary = local.ToUniversalTime() };

      var settings = TaskSchedulerService.ExtractScheduleSettings(trigger);

      Assert.True(settings.SynchronizeTimeZones);
      Assert.Equal(local.TimeOfDay, settings.ScheduleTime);
   }

   [Fact]
   public void ExtractScheduleSettings_LocalStartAndOneTime_AreNotSynchronized()
   {
      var start = new DateTime(2026, 6, 28, 7, 0, 0, DateTimeKind.Unspecified);

      Assert.False(TaskSchedulerService.ExtractScheduleSettings(new DailyTrigger { StartBoundary = start }).SynchronizeTimeZones);
      Assert.False(TaskSchedulerService.ExtractScheduleSettings(new TimeTrigger { StartBoundary = start.ToUniversalTime() })
                                       .SynchronizeTimeZones);
   }

   [Theory]
   [InlineData(0, DaysOfTheWeek.Monday)]
   [InlineData(1, DaysOfTheWeek.Tuesday)]
   [InlineData(2, DaysOfTheWeek.Wednesday)]
   [InlineData(6, DaysOfTheWeek.Sunday)]
   public void DayOfWeekIndex_RoundTrips(int index, DaysOfTheWeek day)
   {
      Assert.Equal(day, TaskSchedulerService.GetDayOfWeekFromIndex(index));
      Assert.Equal(index, TaskSchedulerService.GetIndexFromDayOfWeek(day));
   }

   [Theory]
   [InlineData(0, WhichWeek.FirstWeek)]
   [InlineData(1, WhichWeek.SecondWeek)]
   [InlineData(2, WhichWeek.ThirdWeek)]
   [InlineData(4, WhichWeek.LastWeek)]
   public void WhichWeekIndex_RoundTrips(int index, WhichWeek week)
   {
      Assert.Equal(week, TaskSchedulerService.GetWhichWeekFromIndex(index));
      Assert.Equal(index, TaskSchedulerService.GetIndexFromWhichWeek(week));
   }
}
