// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Concurrent;
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;
using Entree.Core.API;
using Entree.Core.BaseClass;
using Entree.Core.Helpers;
using Entree.MVVM.Model;
using Entree.MVVM.View.Pages;
using Entree.MVVM.ViewModel;
using Entree.MVVM.ViewModel.Control;
using Entree.Services;
using Entree.Tests.Fakes;
using Xunit;
using Xunit.Abstractions;

namespace Entree.Tests;

public class ViewRenderTests(ITestOutputHelper output)
{
   private static readonly string ScreenDirectory = Path.Combine(AppContext.BaseDirectory, "screens");

   public static TheoryData<string, ApplicationTheme> Views()
   {
      var data = new TheoryData<string, ApplicationTheme>();
      foreach (var view in Builders.Keys)
      foreach (var theme in new[] { ApplicationTheme.Light, ApplicationTheme.Dark })
         data.Add(view, theme);
      return data;
   }

   [Theory]
   [MemberData(nameof(Views))]
   public void View_LoadsAndRendersWithoutBindingErrors(string view, ApplicationTheme theme)
   {
      var (errors, file) = UiThread.Run(() => Render(view, theme));
      output.WriteLine($"Screenshot: {file}");
      foreach (var error in errors) output.WriteLine(error);
      Assert.DoesNotContain(errors, IsRealBindingError);
   }

   [Fact]
   public void AppTheme_SwitchesBeforeAndAfterTheWindowLoads()
   {
      var (afterLoad, afterSwitch) = UiThread.Run(() =>
      {
         EnsureApplication();
         var window = OffscreenWindow(null);
         AppTheme.Apply(AppTheme.System, window);
         AppTheme.Apply(AppTheme.Light, window);
         window.Show();
         Settle(100);
         var loaded = ApplicationThemeManager.GetAppTheme();
         AppTheme.Apply(AppTheme.System, window);
         AppTheme.Apply(AppTheme.Dark, window);
         window.Close();
         return (loaded, ApplicationThemeManager.GetAppTheme());
      });
      Assert.Equal(ApplicationTheme.Light, afterLoad);
      Assert.Equal(ApplicationTheme.Dark, afterSwitch);
   }

   private static bool IsRealBindingError(string message) =>
            message.Contains("BindingExpression path error") ||
            message.Contains("is not valid for target property") ||
            message.Contains("Cannot convert");

   private static readonly Dictionary<string, Func<FrameworkElement>> Builders = new()
   {
      ["Home"]     = () => new HomeView { DataContext = new HomeViewModel(new FakeSearchService(), new FakeNavigationService()) },
      ["Search"]   = () => new SearchView { DataContext = SearchViewModelWithItems() },
      ["SearchEmpty"] = () => new SearchView { DataContext = new SearchViewModel(new FakeSearchService(), new FakeNavigationService()) },
      ["Create"]   = () => new CreateConfigView { DataContext = CreateConfigViewModelWithConfig() },
      ["CreateEmpty"] = () => new CreateConfigView { DataContext = new CreateConfigViewModel(new FakeSearchService(), new FakeNavigationService(), new FakeAppSettings()) },
      ["Compare"]  = () => new CompareView { DataContext = new CompareViewModel(new FakeCompareService(), new NotificationService()) },
      ["Tasks"]    = () => new TaskView { DataContext = TaskViewModelWithJobs() },
      ["TasksEmpty"] = () => new TaskView { DataContext = new TaskViewModel(new FakeTaskSchedulerService(), new NotificationService()) },
      ["TaskEditor"] = () => new TaskView { DataContext = TaskViewModelEditing() },
      ["Log"]      = () => new LogView { DataContext = new LogViewModel(new FakeSearchService(), new LogViewerService(new MemorySink()), new NotificationService()) },
      ["LogNotified"] = () => new LogView { DataContext = LogViewModelWithNotification() },
      ["Settings"] = () => new SettingsView { DataContext = new SettingsViewModel(new FakeAppSettings(), new CredentialService(null), new LicenseService(), new NotificationService(), new FakeUpdateService(), new FakeSearchService(), new FakeUserDataService()) }
   };

   private static LogViewModel LogViewModelWithNotification()
   {
      var notifications = new NotificationService();
      var viewModel     = new LogViewModel(new FakeSearchService(), new LogViewerService(new MemorySink()), notifications);
      notifications.Show(InfoBarSeverity.Success, "1,204 paths with 3,877 differences.", "Compare", untilReplaced: true);
      return viewModel;
   }

   private static CreateConfigViewModel CreateConfigViewModelWithConfig()
   {
      var viewModel = new CreateConfigViewModel(new FakeSearchService(), new FakeNavigationService(), new FakeAppSettings());
      viewModel.SearchConfigs.Add(new SearchConfigViewModel(@"\\fileserver\finance", @"C:\Reports", SearchFor.FoldersAndFiles, 3,
                                                            excludedAccounts: ["BUILTIN\\Administrators"]));
      viewModel.SearchConfigs.Add(new SearchConfigViewModel(@"Z:\Shared", @"C:\Reports", SearchFor.Folders, showMatrix: false));
      return viewModel;
   }

   private static SearchViewModel SearchViewModelWithItems()
   {
      var running = new SearchConfigViewModel(@"\\fileserver\finance", @"C:\Reports", SearchFor.FoldersAndFiles, 3,
                                              excludedAccounts: ["BUILTIN\\Administrators"]);
      running.Domains.Add(new DomainContext.Server("fileserver", null));
      running.Domains.Add(new DomainContext.Domain("corp.example.com", new PrincipalContext(ContextType.Machine),
                                                   new SecurityIdentifier("S-1-5-21-10-20-30"), "CORP"));
      running.Domains.Add(new DomainContext.Domain("partner.example.com", new PrincipalContext(ContextType.Machine),
                                                   new SecurityIdentifier("S-1-5-21-40-50-60"), "PARTNER"));
      running.Progress.Phase = "Reading folders";
      foreach (var folder in new[] { "Budget", "Payroll", @"Payroll\2026" })
         running.Progress.EnterFolder($@"\\fileserver\finance\{folder}");
      for (var i = 0; i < 18_204; i++) running.Progress.ItemFound();
      running.IsRunning = true;

      var done = new SearchConfigViewModel(@"D:\Projects", @"C:\Reports", SearchFor.Folders, 1, showMatrix: false) { IsDone = true };
      var service = new FakeSearchService();
      service.SearchConfigs.Add(running);
      service.SearchConfigs.Add(done);
      service.SearchConfigs.Add(new SearchConfigViewModel(@"Z:\Shared", @"C:\Reports", SearchFor.Folders));
      return new SearchViewModel(service, new FakeNavigationService());
   }

   private static TaskViewModel TaskViewModelEditing()
   {
      var viewModel = new TaskViewModel(new FakeTaskSchedulerService(), new NotificationService());
      viewModel.AddNewJobCommand.Execute(new object());
      viewModel.ScheduleHour   = 7;
      viewModel.ScheduleMinute = 5;
      return viewModel;
   }

   private static TaskViewModel TaskViewModelWithJobs() =>
            new(new FakeTaskSchedulerService
                {
                         Jobs =
                         [
                                  new JobViewModel(new Job { Name = "Weekly finance", ScheduleType = ScheduleType.Weekly, NextRunTime = new DateTime(2026, 11, 2, 6, 30, 0) }),
                                  new JobViewModel(new Job { Name = "Once", ScheduleType = ScheduleType.OneTime })
                         ]
                }, new NotificationService());

   private static (List<string> Errors, string File) Render(string view, ApplicationTheme theme)
   {
      EnsureApplication();
      ApplicationThemeManager.Apply(theme, WindowBackdropType.None, false);
      var listener = new BindingErrorListener();
      PresentationTraceSources.Refresh();
      PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
      PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
      var window = OffscreenWindow(Builders[view]());
      try
      {
         window.Show();
         Settle(200);
         OpenEverything(window);
         Settle(800);
         return (listener.Messages.ToList(), Save(window, $"{view}-{theme}.png"));
      }
      finally
      {
         window.Close();
         PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
      }
   }

   private static Window OffscreenWindow(object? content)
   {
      var window = new Window();
      window.BeginInit();
      window.Content       = content;
      window.Width         = 1100;
      window.Height        = 760;
      window.Left          = -20000;
      window.Top           = -20000;
      window.ShowInTaskbar = false;
      window.ShowActivated = false;
      window.WindowStyle   = WindowStyle.None;
      window.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");
      window.EndInit();
      return window;
   }

   private static void EnsureApplication()
   {
      if (Application.Current is not null) return;
      var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
      app.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
      app.Resources.MergedDictionaries.Add(new ControlsDictionary());
      app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Entree;component/Style/Style.xaml") });
   }

   private static void OpenEverything(DependencyObject root)
   {
      foreach (var element in Descendants(root).ToList())
         switch (element)
         {
            case CardExpander expander: expander.IsExpanded = true; break;
            case TabControl tabs:
               for (var i = tabs.Items.Count - 1; i >= 0; i--)
               {
                  tabs.SelectedIndex = i;
                  Settle(100);
               }

               break;
         }
   }

   private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
   {
      for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
      {
         var child = VisualTreeHelper.GetChild(root, i);
         yield return child;
         foreach (var descendant in Descendants(child)) yield return descendant;
      }
   }

   private static void Settle(int milliseconds)
   {
      var frame = new DispatcherFrame();
      var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(milliseconds), DispatcherPriority.ApplicationIdle,
                                      (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
      timer.Start();
      Dispatcher.PushFrame(frame);
      timer.Stop();
   }

   private static string Save(Window window, string name)
   {
      var content    = (FrameworkElement)window.Content;
      var bitmap     = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
      var background = new DrawingVisual();
      using (var context = background.RenderOpen())
         context.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
      bitmap.Render(background);
      bitmap.Render(content);
      Directory.CreateDirectory(ScreenDirectory);
      var path    = Path.Combine(ScreenDirectory, name);
      var encoder = new PngBitmapEncoder();
      encoder.Frames.Add(BitmapFrame.Create(bitmap));
      using var stream = File.Create(path);
      encoder.Save(stream);
      return path;
   }

   private sealed class BindingErrorListener : TraceListener
   {
      public ConcurrentQueue<string> Messages { get; } = new();

      public override void Write(string? message) { }

      public override void WriteLine(string? message)
      {
         if (message is not null) Messages.Enqueue(message);
      }
   }

   private static class UiThread
   {
      private static readonly Lazy<Dispatcher> Dispatcher = new(Start);

      public static T Run<T>(Func<T> work) => Dispatcher.Value.Invoke(work);

      private static Dispatcher Start()
      {
         var started = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
         var thread = new Thread(() =>
         {
            started.SetResult(System.Windows.Threading.Dispatcher.CurrentDispatcher);
            System.Windows.Threading.Dispatcher.Run();
         }) { IsBackground = true };
         thread.SetApartmentState(ApartmentState.STA);
         thread.Start();
         return started.Task.GetAwaiter().GetResult();
      }
   }
}
