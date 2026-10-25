// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using System.Text.Json;
using System.Windows;
using Config.Net;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Entree.Core.BaseClass;
using Entree.Core.Controller;
using Entree.Core.Helpers;
using Entree.MVVM.Model;
using Entree.MVVM.View.Windows;
using Entree.MVVM.ViewModel.Control;
using Entree.Services;
using INavigationService = Entree.Services.INavigationService;
using NavigationService = Entree.Services.NavigationService;

namespace Entree;

public partial class App
{
   private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };
   private readonly MemorySink            _memorySink  = new();
   private readonly ServiceProvider       _serviceProvider;

   public App() => _serviceProvider = ConfigureServices();

   protected override void OnStartup(StartupEventArgs e) =>
            _serviceProvider.GetRequiredService<StartupController>().ProceedStartup(e);

   private ServiceProvider ConfigureServices()
   {
      IServiceCollection services    = new ServiceCollection();
      var                appSettings = ConfigureLogging();

      services.AddSingleton<MainViewModel>();
      services.AddSingleton<HomeViewModel>();
      services.AddSingleton<SettingsViewModel>();
      services.AddSingleton<SearchViewModel>();
      services.AddSingleton<LogViewModel>();
      services.AddSingleton<CreateConfigViewModel>();
      services.AddSingleton<CompareViewModel>();
      services.AddSingleton<TaskViewModel>();

      services.AddSingleton<INavigationService, NavigationService>();
      services.AddSingleton<ICredentialService, CredentialService>();
      services.AddSingleton<ISearchService, SearchService>();
      services.AddSingleton<ICompareService, CompareService>();
      services.AddSingleton<ITaskSchedulerService, TaskSchedulerService>();
      services.AddSingleton(_memorySink);
      services.AddSingleton<LicenseService>();
      services.AddSingleton<LogViewerService>();
      services.AddSingleton<INotificationService, NotificationService>();

      services.AddSingleton<StartupController>();

      services.AddSingleton(appSettings);

      services.AddSingleton<Func<Type, ViewModel>>(serviceProvider => viewModelType =>
                                                            (ViewModel)serviceProvider
                                                                    .GetRequiredService(viewModelType));
      services.AddSingleton<MainWindow>(provider => new MainWindow
                                                    {
                                                             DataContext = provider.GetRequiredService<MainViewModel>()
                                                    });
      return services.BuildServiceProvider();
   }

   private string InitializeAppSettings()
   {
      var baseDirectory = PathHelper.WriteableBaseDirectory();
      var jsonPath      = Path.Combine(baseDirectory, "appsettings.json");
      if (!File.Exists(jsonPath))
         File.WriteAllText(jsonPath, JsonSerializer.Serialize(new AppSettings(), _jsonOptions));
      return jsonPath;
   }

   private IAppSettings ConfigureLogging()
   {
      var jsonPath    = InitializeAppSettings();
      var appSettings = new ConfigurationBuilder<IAppSettings>().UseJsonFile(jsonPath).Build();
      var logPath     = Path.Combine(appSettings.AppLogPath, $"Log_{DateTime.Now:yyyyMMdd_HHmm}.log");

      var logConf = new LoggerConfiguration()
                   .WriteTo.Console()
                   .WriteTo.File(logPath, appSettings.LogLevel, shared: true)
                   .WriteTo.Sink(_memorySink, appSettings.LogLevel);

      Log.Logger = logConf.CreateLogger();
      return appSettings;
   }
}