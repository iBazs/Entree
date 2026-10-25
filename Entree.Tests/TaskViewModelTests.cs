// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.Win32.TaskScheduler;
using Entree.Core.BaseClass;
using Entree.MVVM.Model;
using Entree.MVVM.ViewModel;
using Entree.MVVM.ViewModel.Control;
using Entree.Services;
using Entree.Tests.Fakes;
using Xunit;

namespace Entree.Tests;

public class TaskViewModelTests
{
   private static TaskViewModel NewViewModel() => new(new FakeTaskSchedulerService(), new NotificationService());

   private static TaskScheduleSettings Weekly(DaysOfTheWeek days, int interval, TimeSpan time) =>
            new(new DateTime(2026, 6, 28), time, ScheduleType.Weekly, interval, false,
                days, 0, 1, [], 0, 0, 0);

   private static TaskScheduleSettings MonthlyByDay(int[] days, MonthsOfTheYear months) =>
            new(new DateTime(2026, 1, 15), new TimeSpan(6, 0, 0), ScheduleType.Monthly, 1, true,
                null, 0, days.FirstOrDefault(1), days, months, 0, 0);

   private static JobViewModel JobWith(TaskScheduleSettings schedule, ScheduleType type) =>
            new(new Job { Name = "Test", ScheduleType = type }) { Schedule = schedule };

   [Fact]
   public void EditJob_Weekly_RestoresSelectedDaysIntervalAndTime()
   {
      var vm  = NewViewModel();
      var job = JobWith(Weekly(DaysOfTheWeek.Monday | DaysOfTheWeek.Friday, 2, new TimeSpan(9, 30, 0)),
                        ScheduleType.Weekly);

      vm.EditJobCommand.Execute(job);

      Assert.Equal(2, vm.RecurrenceInterval);
      Assert.Equal(9, vm.ScheduleHour);
      Assert.Equal(30, vm.ScheduleMinute);

      Assert.Equal("Monday", vm.DayCollection[1].Name);
      Assert.Equal("Friday", vm.DayCollection[5].Name);
      Assert.True(vm.DayCollection[1].IsSelected);
      Assert.True(vm.DayCollection[5].IsSelected);
      Assert.Equal(2, vm.DayCollection.Count(d => d.IsSelected));
   }

   [Fact]
   public void EditJob_MonthlyByDayOfMonth_RestoresMonthsAndCalendarDays()
   {
      var vm  = NewViewModel();
      var job = JobWith(MonthlyByDay([1, 15], MonthsOfTheYear.January | MonthsOfTheYear.July),
                        ScheduleType.Monthly);

      vm.EditJobCommand.Execute(job);

      Assert.Equal(0, vm.MonthlyOptionType);
      Assert.Equal("January", vm.MonthCollection[0].Name);
      Assert.Equal("July", vm.MonthCollection[6].Name);
      Assert.True(vm.MonthCollection[0].IsSelected);
      Assert.True(vm.MonthCollection[6].IsSelected);
      Assert.Equal(2, vm.MonthCollection.Count(m => m.IsSelected));

      Assert.True(vm.CalendarDayCollection.Single(d => d.Value == 1).IsSelected);
      Assert.True(vm.CalendarDayCollection.Single(d => d.Value == 15).IsSelected);
      Assert.Equal(2, vm.CalendarDayCollection.Count(d => d.IsSelected));
   }

   [Fact]
   public void EditJob_OpensConfigPanel()
   {
      var vm = NewViewModel();
      vm.EditJobCommand.Execute(JobWith(Weekly(DaysOfTheWeek.Monday, 1, new TimeSpan(8, 0, 0)),
                                        ScheduleType.Weekly));
      Assert.True(vm.IsConfigPanelVisible);
      Assert.NotNull(vm.EditingJob);
   }

   [Fact]
   public void ChangingScheduleHour_PreservesMinute()
   {
      var vm = NewViewModel();
      vm.ScheduleMinute = 45;
      vm.ScheduleHour   = 9;
      Assert.Equal(9, vm.ScheduleHour);
      Assert.Equal(45, vm.ScheduleMinute);

      vm.ScheduleHour = 14;
      Assert.Equal(14, vm.ScheduleHour);
      Assert.Equal(45, vm.ScheduleMinute);
   }

   [Theory]
   [InlineData(24)]
   [InlineData(-1)]
   public void ScheduleHour_RejectsOutOfRange(int badHour)
   {
      var vm   = NewViewModel();
      var hour = vm.ScheduleHour;
      vm.ScheduleHour = badHour;
      Assert.Equal(hour, vm.ScheduleHour);
   }

   [Fact]
   public void SelectionItem_CannotDeselectTheLastSelectedItem()
   {
      var vm        = NewViewModel();
      var onlyChosen = vm.DayCollection.Single(d => d.IsSelected);

      onlyChosen.IsSelected = false;

      Assert.True(onlyChosen.IsSelected);
      Assert.Equal(1, vm.DayCollection.Count(d => d.IsSelected));
   }

   [Fact]
   public void SelectionItem_CanDeselectWhenAnotherIsSelected()
   {
      var vm    = NewViewModel();
      var extra = vm.DayCollection.First(d => !d.IsSelected);

      extra.IsSelected = true;
      Assert.True(vm.DayCollection.Count(d => d.IsSelected) >= 2);

      extra.IsSelected = false;
      Assert.False(extra.IsSelected);
   }

   [Fact]
   public void TimeChoices_AreZeroPaddedAndIndexedByValue()
   {
      var vm = NewViewModel();
      Assert.Equal(24, vm.Hours.Count);
      Assert.Equal(60, vm.Minutes.Count);
      Assert.Equal("07", vm.Hours[7]);
      Assert.Equal("59", vm.Minutes[59]);
   }
}
