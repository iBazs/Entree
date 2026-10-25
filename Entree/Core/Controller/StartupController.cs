// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Entree.Core.Helpers;
using Entree.MVVM.Model;
using Entree.MVVM.View.Windows;
using Entree.MVVM.ViewModel.Control;
using Entree.Services;

namespace Entree.Core.Controller;

public class StartupController(
         ISearchService searchService,
         ICompareService compareService,
         IServiceProvider serviceProvider)
{
   private const int ExitSuccess   = 0;
   private const int ExitFailure   = 1;
   private const int ExitUsage     = 2;
   private const int ParentProcess = -1;
   private const int IdleGraceMs   = 1500;
   private const int StopGraceMs   = 10000;

   private readonly InstanceChannel _instance = new();

   private int         _accepting;
   private bool        _background;
   private int         _exitCode;
   private bool        _shutdownRequested;
   private bool        _silent;
   private TrayIcon?   _tray;
   private MainWindow? _window;

   [DllImport("kernel32.dll")]
   private static extern bool AttachConsole(int dwProcessId);

   public void ProceedStartup(StartupEventArgs e)
   {
      if (IsInstanceRequest(e.Args) && !_instance.IsPrimary)
      {
         _ = ForwardAsync(e.Args);
         return;
      }

      if (_instance.IsPrimary)
         _instance.Listen(args => Application.Current.Dispatcher.InvokeAsync(() => AcceptAsync(args)).Task.Unwrap());
      Run(e.Args);
   }

   private static bool IsSilent(string arg) => CommandLineHelper.IsCommand(arg) && arg.Contains("silent");

   private static string[] WithoutSilent(string[] args) => args.Where(arg => !IsSilent(arg)).ToArray();

   private static bool IsSearch(string[] args) => args.Length > 0 && CommandLineHelper.TrimCommand(args[0]) is "search";

   internal static bool IsInstanceRequest(string[] args)
   {
      var rest = WithoutSilent(args);
      return rest.Length == 0 ? args.Length == 0 : IsSearch(rest) || IsCorrectConfigPath(rest) != null;
   }

   private async Task ForwardAsync(string[] args)
   {
      args = args.Select(arg => CommandLineHelper.IsCommand(arg) ? arg : Path.GetFullPath(arg)).ToArray();
      if (await _instance.TryForwardAsync(args) is { } exitCode)
      {
         ProceedShutdown(exitCode);
         return;
      }

      Run(args);
   }

   private void Run(string[] args)
   {
      if (args.Any(IsSilent)) SetConsoleMode();
      var rest = WithoutSilent(args);

      if (rest.Length == 0)
      {
         if (_silent) ShowHelp();
         else OpenWindow(typeof(HomeViewModel));
      }
      else if (IsCorrectConfigPath(rest) is { } file)
      {
         if (!LoadFile(file)) ProceedShutdown(ExitFailure);
         else OpenWindow(null);
      }
      else if (CommandLineHelper.TrimCommand(rest[0]) is "compare")
      {
         if (!_silent) OpenWindow(typeof(CompareViewModel));
         Task.Run(async () => await HandleCompare(rest));
      }
      else if (IsSearch(rest) && _silent)
      {
         _background = true;
         ShowTray("Entrée is running scheduled searches");
         _ = RunInBackgroundAsync(rest);
      }
      else if (IsSearch(rest))
      {
         OpenWindow(typeof(SearchViewModel));
         _ = SearchAsync(rest, false, true);
      }
      else
      {
         ShowHelp();
      }
   }

   private async Task<int> AcceptAsync(string[] args)
   {
      var silent = args.Any(IsSilent);
      var rest   = WithoutSilent(args);
      if (rest.Length == 0)
      {
         OpenWindow(null);
         return ExitSuccess;
      }

      if (IsCorrectConfigPath(rest) is { } file)
      {
         OpenWindow(null);
         return LoadFile(file) ? ExitSuccess : ExitFailure;
      }

      if (!IsSearch(rest)) return ExitUsage;
      if (!silent) OpenWindow(typeof(SearchViewModel));
      else if (_window is not { IsVisible: true }) ShowTray("Entrée is running scheduled searches");
      _accepting++;
      try
      {
         return await SearchAsync(rest, silent, false);
      }
      finally
      {
         _accepting--;
      }
   }

   private async Task RunInBackgroundAsync(string[] args)
   {
      _exitCode = await SearchAsync(args, true, true);
      await ShutdownWhenIdleAsync();
   }

   private async Task ShutdownWhenIdleAsync()
   {
      do
      {
         await searchService.WhenIdle();
         await Task.Delay(IdleGraceMs);
      } while (HasWork);

      if (_window is { IsVisible: true }) return;
      ProceedShutdown(_exitCode);
   }

   private void OpenWindow(Type? page)
   {
      if (_window is null)
      {
         _window = serviceProvider.GetRequiredService<MainWindow>();
         AppTheme.Apply(serviceProvider.GetRequiredService<IAppSettings>().Theme, _window);
         _window.Closing += MainWindow_OnClosing;
      }

      if (!_window.IsVisible) _window.Show();
      if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
      _window.Activate();
      if (page is not null) serviceProvider.GetRequiredService<INavigationService>().NavigateTo(page);
   }

   private bool HasWork => searchService.IsSearchRunning || _accepting > 0;

   private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
   {
      if (!_background && !HasWork) return;
      e.Cancel = true;
      _window?.Hide();
      FinishInTray();
   }

   private void FinishInTray()
   {
      _background = true;
      ShowTray(HasWork ? "Entrée is finishing searches" : "Entrée");
      _ = ShutdownWhenIdleAsync();
   }

   private void ShowTray(string tooltip)
   {
      _tray ??= new TrayIcon(() => OpenWindow(typeof(SearchViewModel)), () => _ = ExitFromTrayAsync());
      _tray.Show(tooltip);
   }

   private async Task ExitFromTrayAsync()
   {
      var running = searchService.IsSearchRunning;
      if (running)
      {
         searchService.Stop();
         await Task.WhenAny(searchService.WhenIdle(), Task.Delay(StopGraceMs));
      }

      ProceedShutdown(running ? ExitFailure : _exitCode, true);
   }

   private void ProceedShutdown(int exitCode, bool force = false)
   {
      var app = Application.Current;
      app?.Dispatcher.Invoke(() =>
      {
         if (_shutdownRequested) return;
         if (!force && HasWork)
         {
            _exitCode = Math.Max(_exitCode, exitCode);
            FinishInTray();
            return;
         }

         _shutdownRequested = true;
         Log.Information("Exiting with code {ExitCode}", exitCode);
         _tray?.Dispose();
         _instance.Dispose();
         Log.CloseAndFlush();
         app.Shutdown(exitCode);
      });
   }

   private void ShowHelp()
   {
      Console.WriteLine("\nAvailable commands:");
      Console.WriteLine("  --help                                      Show this help message");
      Console.WriteLine("  --search <file.entree>                      Run the searches saved in a configuration file");
      Console.WriteLine("  --compare <older> <newer> --xlsx            Compare two reports and export to xlsx, or default to csv");
      Console.WriteLine("  --silent                                    Run without a window\n");
      ProceedShutdown(ExitUsage);
   }

   private async Task HandleCompare(string[] args)
   {
      Log.Debug("Compare Command with args: {Args}", string.Join(" ", args));
      var paths        = args.Where(x => !CommandLineHelper.IsCommand(x)).ToList();
      var exportAsXlsx = args.Any(x => CommandLineHelper.IsCommand(x) && x.Contains("xlsx"));
      switch (paths.Count)
      {
         case < 2:
            Log.Error("Compare command requires exactly 2 paths, received: {PathCount}", paths.Count);
            Console.WriteLine("Compare command requires exactly 2 paths, received: {0}", paths.Count);
            ShowHelp();
            return;
         case > 2:
            Log.Warning("Received {PathCount} paths for comparing, using only first 2", paths.Count);
            Console.WriteLine($"Too many file paths ({paths.Count}) for comparing. Using first 2 paths.");
            break;
      }

      var sourcePath = paths[0].Trim();
      var newPath    = paths[1].Trim();

      Log.Information("Comparing files: original:{SourcePath}", sourcePath);
      Log.Information("new:{NewPath}", newPath);
      Console.WriteLine("Comparing files: original:{0}\n new:{1}\n", sourcePath, newPath);

      try
      {
         if (_silent)
         {
            await await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
               Console.WriteLine("Starting file comparison. This may take a while.");
               await compareService.CompareFilesAsync(sourcePath, newPath);
               Console.WriteLine("Comparison Done. Exporting results...");
               var outputPath = await compareService.ExportResultsAsync(exportAsXlsx);
               Log.Information("File comparison completed, results in {OutputPath}", outputPath);
               Console.WriteLine($"File comparison completed: {outputPath}");
               ProceedShutdown(ExitSuccess);
            });
         }
         else
         {
            await await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
               var vm = serviceProvider.GetRequiredService<CompareViewModel>();
               vm.OriginalFilePath = sourcePath;
               vm.NewFilePath      = newPath;
               await vm.CompareAsync();
               await vm.ExportResultsAsync(exportAsXlsx ? "xlsx" : "csv");
               PathHelper.OpenFile(vm.OutputFilePath);
            });
         }
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Error during file comparison: {message}", ex.Message);
         Console.WriteLine("Error during file comparison. See logs for details.");
         ProceedShutdown(ExitFailure);
      }
   }

   private async Task<int> SearchAsync(string[] args, bool silent, bool waitForResult)
   {
      Log.Debug("Search Command with args: {FilePath}", string.Join(" ", args));
      switch (args)
      {
         case { Length: < 2 }:
            Log.Error("Search configuration file path cannot be empty");
            Console.WriteLine("Search configuration file path cannot be empty.");
            return ExitUsage;
         case { Length: > 2 }:
            Log.Error("Too many arguments for search: {Args}", string.Join(" ", args));
            Console.WriteLine("Too many arguments for search.");
            return ExitUsage;
      }

      try
      {
         var searchConfigs = await Task.Run(() => ReportHelper.LoadSearchConfigurations(args[1]));
         if (searchConfigs.Count == 0)
         {
            Log.Warning("No valid search configurations found in file: {FilePath}", args[1]);
            Console.WriteLine("No valid search configurations found in the file.");
            return ExitFailure;
         }

         if (silent)
            foreach (var config in searchConfigs) config.OpenResult = false;
         Log.Information("Queued {ConfigCount} search configurations from {FilePath}", searchConfigs.Count, args[1]);
         var search = LogOutcomeAsync(searchService.Search(searchConfigs), args[1]);
         return waitForResult ? await search : ExitSuccess;
      }
      catch (FileNotFoundException)
      {
         Log.Error("Search configuration file not found: {FilePath}", args[1]);
         Console.WriteLine($"File not found: {args[1]}");
      }
      catch (Exception e)
      {
         Log.Error(e, "Search from {FilePath} failed", args[1]);
         Console.WriteLine("Search failed. See logs for details.");
      }

      return ExitFailure;
   }

   private static async Task<int> LogOutcomeAsync(Task<bool> search, string file)
   {
      var succeeded = await search;
      Log.Information("Search from {FilePath} finished {Result}", file, succeeded ? "successfully" : "with errors");
      return succeeded ? ExitSuccess : ExitFailure;
   }

   private static string? IsCorrectConfigPath(string[] args)
   {
      if (args.Length == 1 &&
          !CommandLineHelper.IsCommand(args[0]) &&
          Path.GetExtension(args[0]).Equals(".entree", StringComparison.OrdinalIgnoreCase))
         return args[0];

      return null;
   }

   private bool LoadFile(string filePath)
   {
      try
      {
         var searchConfigs = ReportHelper.LoadSearchConfigurations(filePath);
         Log.Information("Successfully loaded {ConfigCount} search configurations", searchConfigs.Count);

         var createConfigViewModel = serviceProvider.GetRequiredService<CreateConfigViewModel>();
         createConfigViewModel.DragDropLoadConfigFileCommand.Execute(filePath);

         serviceProvider.GetRequiredService<INavigationService>().NavigateTo<CreateConfigViewModel>();
         return true;
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Failed to open file: {FilePath}", filePath);
         MessageBox.Show($"Failed to open file: {ex.Message}",
                         "Error", MessageBoxButton.OK, MessageBoxImage.Error);
         return false;
      }
   }

   private void SetConsoleMode()
   {
      _silent = true;
      AttachConsole(ParentProcess);
      serviceProvider.GetRequiredService<CompareViewModel>().SetConsoleMode(true);
   }
}
