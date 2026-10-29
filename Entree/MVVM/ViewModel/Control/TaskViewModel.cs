// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using Serilog;
using Entree.Core.BaseClass;
using Entree.MVVM.Model;
using Entree.Services;
using Wpf.Ui.Controls;
using Task = System.Threading.Tasks.Task;

namespace Entree.MVVM.ViewModel.Control;

public class TaskViewModel : Core.BaseClass.ViewModel
{
   private readonly INotificationService  _notifications;
   private readonly ITaskSchedulerService _taskSchedulerService;
   private          bool                  _canRefresh = true;
   private          string?               _originalName;

   public TaskViewModel(ITaskSchedulerService taskSchedulerService, INotificationService notifications)
   {
      _taskSchedulerService = taskSchedulerService;
      _notifications        = notifications;

      // ReSharper disable AsyncVoidLambda
      RefreshCommand          = new RelayCommand(async _ => await LoadJobs(), _ => CanRefresh);
      AddNewJobCommand        = new RelayCommand(_ => AddNewJob());
      EditJobCommand          = new RelayCommand(EditJob);
      DeleteJobCommand        = new RelayCommand(DeleteJob);
      SaveTaskCommand         = new RelayCommand(async _ => await SaveTask(), _ => CanSaveTask());
      CancelCommand           = new RelayCommand(_ => CancelEdit());
      BrowseSearchFileCommand = new RelayCommand(_ => BrowseSearchFile());
      // ReSharper restore AsyncVoidLambda

      InitializeCollections();
      ResetDateTime();
      SelectedScheduleTypeIndex = 0;

      _ = InitializeAsync();
   }

   public ObservableCollection<SelectionItem> CalendarDayCollection { get; } = [];
   public ObservableCollection<SelectionItem> DayCollection         { get; } = [];
   public ObservableCollection<SelectionItem> MonthCollection       { get; } = [];

   public RelayCommand RefreshCommand   { get; }
   public RelayCommand AddNewJobCommand { get; }

   // ReSharper disable once UnusedAutoPropertyAccessor.Global
   public RelayCommand EditJobCommand          { get; }
   public RelayCommand DeleteJobCommand        { get; }
   public RelayCommand SaveTaskCommand         { get; }
   public RelayCommand CancelCommand           { get; }
   public RelayCommand BrowseSearchFileCommand { get; }

   private async Task InitializeAsync()
   {
      try
      {
         await _taskSchedulerService.EnsureTaskFolderExistsAsync();
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Could not prepare the task scheduler folder.");
      }

      await LoadJobs();
   }

   private void InitializeCollections()
   {
      for (var i = 0; i < 7; i++)
         DayCollection.Add(new SelectionItem(((DayOfWeek)i).ToString(),
                                             DateTime.Now.DayOfWeek == (DayOfWeek)i,
                                             DayCollection));

      var months = Enum.GetValues(typeof(MonthsOfTheYear)).Cast<MonthsOfTheYear>().ToArray();
      for (var i = 0; i < months.Length - 1; i++)
         MonthCollection.Add(new SelectionItem(months[i].ToString(), DateTime.Now.Month - 1 == i, MonthCollection));

      for (var i = 1; i <= 31; i++)
         CalendarDayCollection.Add(new SelectionItem(i.ToString(), DateTime.Now.Day == i, CalendarDayCollection,
                                                     i));
   }

   private async Task LoadJobs()
   {
      if (!CanRefresh) return;
      CanRefresh = false;
      Jobs.Clear();
      try
      {
         var jobViewModels = await _taskSchedulerService.LoadJobsAsync();
         foreach (var jobViewModel in jobViewModels) Jobs.Add(jobViewModel);
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Error loading jobs from service.");
      }

      CanRefresh = true;
   }

   private void AddNewJob()
   {
      ResetDateTime();
      ResetSchedule();
      IsNewJob      = true;
      _originalName = null;
      var newJob = new Job
                   {
                            Name               = "New Search Task",
                            Status             = "New",
                            SearchFilePath     = "",
                            RunElevated        = false,
                            NextRunTime        = GetScheduledDateTime(),
                            ScheduleType       = (ScheduleType)SelectedScheduleTypeIndex
                   };

      EditingJob           = new JobViewModel(newJob);
      IsConfigPanelVisible = true;
      UpdateCommandPreview();
   }

   private void ResetSchedule()
   {
      var today = DateTime.Now;
      SelectedScheduleTypeIndex = 0;
      RecurrenceInterval        = 1;
      SynchronizeTimeZones      = false;
      MonthlyOptionType         = 0;
      DayOfMonth                = today.Day;
      MonthWeekNumber           = 0;
      MonthDayOfWeek            = 0;
      ApplySelection(DayCollection, i => (int)today.DayOfWeek == i);
      ApplySelection(MonthCollection, i => today.Month - 1 == i);
      ApplySelection(CalendarDayCollection, i => today.Day == i + 1);
   }

   private void EditJob(object obj)
   {
      if (obj is not JobViewModel jobViewModel) return;
      IsNewJob      = false;
      _originalName = jobViewModel.Name;

      var newJob = new Job
                   {
                            Name           = jobViewModel.Name,
                            Status         = jobViewModel.Status,
                            SearchFilePath = jobViewModel.SearchFilePath,
                            IsRunning      = jobViewModel.IsRunning,
                            NextRunTime    = jobViewModel.NextRunTime,
                            ScheduleType   = jobViewModel.ScheduleType,
                            CommandLine    = jobViewModel.CommandLine,
                            RunElevated    = jobViewModel.RunElevated
                   };

      EditingJob = new JobViewModel(newJob);

      if (jobViewModel.NextRunTime.HasValue)
      {
         ScheduleDate = jobViewModel.NextRunTime.Value.Date;
         ScheduleTime = jobViewModel.NextRunTime.Value.TimeOfDay;
      }

      SelectedScheduleTypeIndex = (int)jobViewModel.ScheduleType;

      if (jobViewModel.Schedule is { } schedule) ApplyScheduleSettings(schedule);

      IsConfigPanelVisible = true;
      UpdateCommandPreview();
   }

   private void ApplyScheduleSettings(TaskScheduleSettings schedule)
   {
      RecurrenceInterval   = schedule.RecurrenceInterval;
      SynchronizeTimeZones = schedule.SynchronizeTimeZones;
      MonthlyOptionType    = schedule.MonthlyOptionType;
      MonthDayOfWeek       = schedule.MonthDayOfWeek;
      MonthWeekNumber      = schedule.MonthWeekNumber;
      DayOfMonth           = schedule.DayOfMonth;
      ScheduleDate         = schedule.ScheduleDate;
      ScheduleTime         = schedule.ScheduleTime;

      if (schedule.ScheduleType == ScheduleType.Weekly && schedule.SelectedDaysOfWeek is { } days)
         ApplySelection(DayCollection, i => (days & (DaysOfTheWeek)(1 << i)) != 0);

      if (schedule.ScheduleType != ScheduleType.Monthly) return;

      ApplySelection(MonthCollection, i => (schedule.SelectedMonths & (MonthsOfTheYear)(1 << i)) != 0);

      var selectedDays = schedule.SelectedDays;
      if (schedule.MonthlyOptionType == 0 && selectedDays.Length > 0)
         ApplySelection(CalendarDayCollection, i => selectedDays.Contains(CalendarDayCollection[i].Value));
   }

   private static void ApplySelection(ObservableCollection<SelectionItem> collection, Func<int, bool> wantedAt)
   {
      for (var i = 0; i < collection.Count; i++)
         collection[i].SetSelected(wantedAt(i));
   }

   private async void DeleteJob(object obj)
   {
      if (obj is not JobViewModel jobViewModel) return;
      try
      {
         var success = await _taskSchedulerService.DeleteJobAsync(jobViewModel.Name);
         if (!success) return;
         Jobs.Remove(jobViewModel);
         IsConfigPanelVisible = false;
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Could not delete the task({Name}). {Message}", jobViewModel.Name, ex.Message);
      }
   }

   private bool _isSaving;

   private async Task SaveTask()
   {
      if (EditingJob == null || _isSaving) return;

      _isSaving = true;
      try
      {
         if (EditingJob.ScheduleType == ScheduleType.Weekly) ValidateWeeklySelection();
         else if (EditingJob.ScheduleType == ScheduleType.Monthly) ValidateMonthSelection();

         if (SaveProblem() is { } problem)
         {
            _notifications.Show(InfoBarSeverity.Warning, problem, "Task not saved");
            return;
         }

         var settings = CreateTaskScheduleSettings();
         if (!await _taskSchedulerService.SaveJobAsync(EditingJob, settings))
         {
            _notifications.Show(InfoBarSeverity.Error, "Task Scheduler refused the task. See the log for details.",
                                "Task not saved");
            return;
         }

         if (_originalName is not null && _originalName != EditingJob.Name)
            await _taskSchedulerService.DeleteJobAsync(_originalName);
         IsConfigPanelVisible = false;
         EditingJob           = null;
         await LoadJobs();
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Error saving task '{TaskName}': {Error}", EditingJob?.Name, ex.Message);
      }
      finally
      {
         _isSaving = false;
      }
   }

   private TaskScheduleSettings CreateTaskScheduleSettings()
   {
      var selectedDays = CalendarDayCollection.Where(x => x.IsSelected)
                                              .Select(kvp => kvp.Value)
                                              .ToArray();

      return new TaskScheduleSettings(
                                      ScheduleDate,
                                      ScheduleTime,
                                      EditingJob!.ScheduleType,
                                      RecurrenceInterval,
                                      SynchronizeTimeZones,
                                      (DaysOfTheWeek)SelectedMask(DayCollection),
                                      MonthlyOptionType,
                                      DayOfMonth,
                                      selectedDays,
                                      (MonthsOfTheYear)SelectedMask(MonthCollection),
                                      MonthWeekNumber,
                                      MonthDayOfWeek
                                     );
   }

   private string? SaveProblem()
   {
      var name = EditingJob!.Name;
      if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "A task name cannot contain \\ / : * ? \" < > |";
      if (name != _originalName && Jobs.Any(job => job.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
         return $"A task named {name} already exists.";
      return EditingJob.ScheduleType == ScheduleType.OneTime && ScheduleDate.Date.Add(ScheduleTime) <= DateTime.Now
                      ? "A one time task needs a start in the future."
                      : null;
   }

   private bool CanSaveTask()
   {
      if (EditingJob == null || _isSaving) return false;

      return !string.IsNullOrWhiteSpace(EditingJob.Name) &&
             !string.IsNullOrWhiteSpace(EditingJob.SearchFilePath);
   }

   private void CancelEdit()
   {
      EditingJob           = null;
      IsConfigPanelVisible = false;
   }

   private void BrowseSearchFile()
   {
      var dlg = new OpenFileDialog { DefaultExt = ".entree", Filter = "Entrée Files|*.entree" };
      if (dlg.ShowDialog() != true) return;
      if (EditingJob != null) EditingJob.SearchFilePath = dlg.FileName;
      UpdateCommandPreview();
   }

   private DateTime GetScheduledDateTime()
   {
      var scheduledDateTime = ScheduleDate.Date.Add(ScheduleTime);
      return scheduledDateTime < DateTime.Now ? DateTime.Now : scheduledDateTime;
   }

   private void ResetDateTime()
   {
      var now = DateTime.Now;
      ScheduleDate = now.Date;
      ScheduleTime = now.TimeOfDay;

      if (GetScheduledDateTime() <= now) ScheduleTime = now.AddMinutes(1).TimeOfDay;
   }

   private void UpdateCommandPreview()
   {
      if (EditingJob == null)
      {
         CommandPreview = string.Empty;
         return;
      }

      CommandPreview         = TaskSchedulerService.BuildCommandLineArguments(EditingJob.SearchFilePath);
      EditingJob.CommandLine = CommandPreview;
   }

   private void UpdateRecurrenceVisibility()
   {
      IsRecurrenceEnabled = SelectedScheduleTypeIndex > 0;
      OnPropertyChanged(nameof(IsRecurrenceEnabled));
      OnPropertyChanged(nameof(IsWeeklyScheduleVisible));
      OnPropertyChanged(nameof(IsMonthlyScheduleVisible));
      OnPropertyChanged(nameof(IsDayOfMonthOptionVisible));
      OnPropertyChanged(nameof(IsDayOfWeekOptionVisible));
      OnPropertyChanged(nameof(IsRecurrenceIntervalVisible));
      OnPropertyChanged(nameof(RecurrenceUnit));
      Log.Debug("Updated recurrence visibility settings based on schedule type: {ScheduleType}",
                (ScheduleType)SelectedScheduleTypeIndex);
   }

   private void ValidateMonthSelection()
   {
      if (SelectedScheduleTypeIndex != 3) return;
      if (MonthCollection.Any(x => x.IsSelected)) return;

      Log.Warning("No months selected for monthly schedule, defaulting to current month");
      MonthCollection[DateTime.Now.Month - 1].IsSelected = true;
   }

   private void ValidateWeeklySelection()
   {
      if (SelectedScheduleTypeIndex != 2) return;
      if (DayCollection.Any(x => x.IsSelected)) return;
      Log.Warning("No days selected for weekly schedule, defaulting to Monday");
      DayCollection[(int)DayOfWeek.Monday].IsSelected = true;
   }

   private static int SelectedMask(IList<SelectionItem> items) =>
            items.Select((item, index) => item.IsSelected ? 1 << index : 0).Aggregate(0, (mask, bit) => mask | bit);

   public class SelectionItem : ObservableObject
   {
      private readonly ObservableCollection<SelectionItem> _parentCollection;
      private          bool                                _isSelected;

      public SelectionItem(string name, bool isSelected, ObservableCollection<SelectionItem> parentCollection,
                           int value = 0)
      {
         _parentCollection = parentCollection;
         Name              = name;
         IsSelected        = isSelected;
         Value             = value;
      }

      public string Name { get; }

      public bool IsSelected
      {
         get => _isSelected;
         set
         {
            if (_isSelected == value) return;
            if (!value && _parentCollection.Count(x => x.IsSelected) <= 1) return;
            _isSelected = value;
            OnPropertyChanged();
         }
      }

      internal void SetSelected(bool value)
      {
         if (_isSelected == value) return;
         _isSelected = value;
         OnPropertyChanged(nameof(IsSelected));
      }

      public int Value { get; }
   }

   private JobViewModel? _editingJob;
   private bool          _isConfigPanelVisible;
   private bool          _isNewJob;
   private string        _commandPreview = string.Empty;

   private DateTime _scheduleDate = DateTime.Now;
   private TimeSpan _scheduleTime = DateTime.Now.TimeOfDay;
   private int      _selectedScheduleTypeIndex;
   private bool     _synchronizeTimeZones;
   private bool     _isRecurrenceEnabled  = true;
   private int      _recurrenceInterval   = 1;

   private int _monthlyOptionType;
   private int _dayOfMonth = DateTime.Now.Day;
   private int _monthDayOfWeek;
   private int _monthWeekNumber;

   public ObservableCollection<JobViewModel> Jobs { get; } = [];

   public int RecurrenceInterval
   {
      get => _recurrenceInterval;
      set
      {
         if (_recurrenceInterval == value) return;
         _recurrenceInterval = Math.Max(1, value);
         OnPropertyChanged();
         UpdateCommandPreview();
      }
   }

   public bool SynchronizeTimeZones
   {
      get => _synchronizeTimeZones;
      set => SetIfChanged(ref _synchronizeTimeZones, value);
   }

   public bool IsRecurrenceEnabled
   {
      get => _isRecurrenceEnabled;
      set
      {
         if (_isRecurrenceEnabled == value) return;
         _isRecurrenceEnabled = value;
         OnPropertyChanged();
         UpdateRecurrenceVisibility();
      }
   }

   public int MonthlyOptionType
   {
      get => _monthlyOptionType;
      set
      {
         if (_monthlyOptionType == value) return;
         _monthlyOptionType = value;
         OnPropertyChanged();
         OnPropertyChanged(nameof(IsDayOfMonthOptionVisible));
         OnPropertyChanged(nameof(IsDayOfWeekOptionVisible));
         UpdateCommandPreview();
      }
   }

   // ReSharper disable once MemberCanBePrivate.Global
   public int DayOfMonth
   {
      get => _dayOfMonth;
      set
      {
         if (_dayOfMonth == value) return;
         _dayOfMonth = Math.Clamp(value, 1, 31);
         OnPropertyChanged();
         UpdateCommandPreview();
      }
   }

   public int MonthWeekNumber
   {
      get => _monthWeekNumber;
      set
      {
         if (_monthWeekNumber == value) return;
         _monthWeekNumber = value;
         OnPropertyChanged();
         UpdateCommandPreview();
      }
   }

   public int MonthDayOfWeek
   {
      get => _monthDayOfWeek;
      set
      {
         if (_monthDayOfWeek == value) return;
         _monthDayOfWeek = value;
         OnPropertyChanged();
         UpdateCommandPreview();
      }
   }

   public bool IsWeeklyScheduleVisible     => SelectedScheduleTypeIndex == 2;
   public string RecurrenceUnit            => SelectedScheduleTypeIndex == 2 ? "weeks" : "days";
   public bool IsMonthlyScheduleVisible    => SelectedScheduleTypeIndex == 3;
   public bool IsDayOfMonthOptionVisible   => IsMonthlyScheduleVisible && MonthlyOptionType == 0;
   public bool IsDayOfWeekOptionVisible    => IsMonthlyScheduleVisible && MonthlyOptionType == 1;
   public bool IsRecurrenceIntervalVisible => SelectedScheduleTypeIndex > 0;

   public bool IsNewJob
   {
      get => _isNewJob;
      private set => Set(ref _isNewJob, value);
   }

   public JobViewModel? EditingJob
   {
      get => _editingJob;
      private set
      {
         if (_editingJob == value) return;
         _editingJob = value;
         OnPropertyChanged();
         UpdateCommandPreview();
      }
   }

   public bool IsConfigPanelVisible
   {
      get => _isConfigPanelVisible;
      set => SetIfChanged(ref _isConfigPanelVisible, value);
   }

   public DateTime ScheduleDate
   {
      get => _scheduleDate;
      set
      {
         if (_scheduleDate == value) return;
         _scheduleDate = value;
         OnPropertyChanged();
         UpdateCommandPreview();
      }
   }

   private static readonly string[] HourChoices   = TwoDigits(24);
   private static readonly string[] MinuteChoices = TwoDigits(60);

   public IReadOnlyList<string> Hours   => HourChoices;
   public IReadOnlyList<string> Minutes => MinuteChoices;

   private static string[] TwoDigits(int count) => Enumerable.Range(0, count).Select(i => i.ToString("00")).ToArray();

   public int ScheduleHour
   {
      get => _scheduleTime.Hours;
      set
      {
         if (value is < 0 or > 23 || value == _scheduleTime.Hours) return;
         ScheduleTime = new TimeSpan(value, _scheduleTime.Minutes, 0);
      }
   }

   public int ScheduleMinute
   {
      get => _scheduleTime.Minutes;
      set
      {
         if (value is < 0 or > 59 || value == _scheduleTime.Minutes) return;
         ScheduleTime = new TimeSpan(_scheduleTime.Hours, value, 0);
      }
   }

   private TimeSpan ScheduleTime
   {
      get => _scheduleTime;
      set
      {
         if (_scheduleTime == value) return;
         _scheduleTime = value;
         OnPropertyChanged();
         OnPropertyChanged(nameof(ScheduleHour));
         OnPropertyChanged(nameof(ScheduleMinute));
         UpdateCommandPreview();
      }
   }

   public int SelectedScheduleTypeIndex
   {
      get => _selectedScheduleTypeIndex;
      set
      {
         if (_selectedScheduleTypeIndex == value) return;
         _selectedScheduleTypeIndex = value;

         if (EditingJob != null) EditingJob.ScheduleType = (ScheduleType)value;

         OnPropertyChanged();
         OnPropertyChanged(nameof(IsWeeklyScheduleVisible));
         OnPropertyChanged(nameof(IsRecurrenceIntervalVisible));
         UpdateRecurrenceVisibility();
         UpdateCommandPreview();

         Log.Debug("Schedule type changed to {ScheduleType}", (ScheduleType)value);
      }
   }

   public string CommandPreview
   {
      get => _commandPreview;
      private set => SetIfChanged(ref _commandPreview, value);
   }

   public bool CanRefresh
   {
      get => _canRefresh;
      set => Set(ref _canRefresh, value);
   }
}