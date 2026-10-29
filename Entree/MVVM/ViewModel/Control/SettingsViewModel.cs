// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Input;
using Microsoft.Win32;
using Serilog;
using Wpf.Ui.Controls;
using Entree.Core.Helpers;
using Entree.MVVM.Model;
using Entree.Services;
using RelayCommand = Entree.Core.BaseClass.RelayCommand;

// ReSharper disable AsyncVoidLambda

namespace Entree.MVVM.ViewModel.Control;

public class SettingsViewModel : Core.BaseClass.ViewModel
{
   private readonly ICredentialService   _credentialService;
   private readonly LicenseService       _licenseService;
   private readonly INotificationService _notifications;
   private readonly ISearchService       _searchService;
   private readonly IAppSettings         _settings;
   private readonly IUpdateService       _updates;
   private readonly IUserDataService     _userData;
   private          string?              _availableVersion;
   private          string               _domainPassword = string.Empty;
   private          string?              _domainPath     = string.Empty;
   private          string               _domainUsername = string.Empty;
   private          bool                 _isUpdating;
   private          bool                 _isValidating;
   private          string               _updateStatus;

   public SettingsViewModel(IAppSettings settings, ICredentialService credentialService, LicenseService licenseService,
                            INotificationService notifications, IUpdateService updates, ISearchService searchService,
                            IUserDataService userData)
   {
      _userData               = userData;
      DeleteAllDataCommand    = new RelayCommand(async _ => { await DeleteAllDataAsync(); });
      _notifications          = notifications;
      _settings               = settings;
      _credentialService      = credentialService;
      _licenseService         = licenseService;
      _updates                = updates;
      _searchService          = searchService;
      _updateStatus = updates.IsInstalled ? "Look for a newer version" : "Portable build, new versions are on GitHub";
      UpdateCommand = new RelayCommand(async _ => { await UpdateAsync(); }, _ => !IsUpdating);
      SelectLogPathCommand    = new RelayCommand(_ => SelectPath(true));
      SelectResultPathCommand = new RelayCommand(_ => SelectPath(false));

      ValidateDomainCommand          = new RelayCommand(async _ => { await ValidateDomainAsync(); }, _ => !IsValidating);
      AddDomainCommand               = new RelayCommand(async _ => { await AddDomainAsync(); }, _ => !IsValidating);
      RemoveDomainCommand            = new RelayCommand(async o => { await RemoveDomainAsync(o); });
      ShowLicenseCommand             = new RelayCommand(async _ => { await ShowLicenseAsync(); });
      ShowThirdPartyLibrariesCommand = new RelayCommand(async _ => { await ShowThirdPartyLicenseAsync(); });
   }

   public int    MaxParallelSize { get; } = Environment.ProcessorCount * 2;
   public string AppVersion      { get; } =
            FormatVersion(Assembly.GetExecutingAssembly()
                                  .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

   public string LogPath
   {
      get => _settings.AppLogPath;
      private set
      {
         _settings.AppLogPath = value;
         OnPropertyChanged();
      }
   }

   public string ResultPath
   {
      get => _settings.AppResultPath;
      private set
      {
         _settings.AppResultPath = value;
         OnPropertyChanged();
      }
   }

   public IReadOnlyList<string> ThemeChoices => AppTheme.Choices;

   public string Theme
   {
      get => _settings.Theme;
      set
      {
         if (_settings.Theme == value) return;
         _settings.Theme = value;
         AppTheme.Apply(value, System.Windows.Application.Current?.MainWindow);
         OnPropertyChanged();
      }
   }

   public int ThreadLimit
   {
      get => _settings.ThreadLimit;
      set
      {
         _settings.ThreadLimit = value;
         OnPropertyChanged();
      }
   }

   public string? DomainPath
   {
      get => _domainPath;
      set => Set(ref _domainPath, value);
   }

   public string DomainUsername
   {
      get => _domainUsername;
      set => Set(ref _domainUsername, value);
   }

   public string DomainPassword
   {
      get => _domainPassword;
      set => Set(ref _domainPassword, value);
   }

   public bool IsValidating
   {
      get => _isValidating;
      set
      {
         Set(ref _isValidating, value);
         CommandManager.InvalidateRequerySuggested();
      }
   }


   public bool IsUpdating
   {
      get => _isUpdating;
      private set
      {
         Set(ref _isUpdating, value);
         CommandManager.InvalidateRequerySuggested();
      }
   }

   public string UpdateStatus
   {
      get => _updateStatus;
      private set => Set(ref _updateStatus, value);
   }

   public string UpdateAction =>
            !_updates.IsInstalled        ? "Download new versions"
            : _availableVersion is { } v ? $"Install {v} and restart"
                                           : "Check for updates";

   public ObservableCollection<SecureCredential> Domains => _credentialService.GetObservableCredentials();

   public RelayCommand ValidateDomainCommand          { get; }
   public RelayCommand RemoveDomainCommand            { get; }
   public RelayCommand AddDomainCommand               { get; }
   public RelayCommand SelectResultPathCommand        { get; }
   public RelayCommand SelectLogPathCommand           { get; }
   public RelayCommand ShowLicenseCommand             { get; }
   public RelayCommand ShowThirdPartyLibrariesCommand { get; }
   public RelayCommand UpdateCommand                  { get; }
   public RelayCommand DeleteAllDataCommand           { get; }

   internal Func<Task<bool>> ConfirmDeleteAll { get; set; } = ConfirmDeleteAllAsync;
   internal Action           Exit             { get; set; } = () => System.Windows.Application.Current.Shutdown();

   private async Task DeleteAllDataAsync()
   {
      if (_searchService.IsSearchRunning)
      {
         _notifications.Show(InfoBarSeverity.Warning, "Wait for the running searches to finish first.",
                             "Searches are running");
         return;
      }

      if (!await ConfirmDeleteAll()) return;
      if (await _userData.DeleteAllAsync())
      {
         Exit();
         return;
      }

      _notifications.Show(InfoBarSeverity.Error, "Some scheduled tasks or files could not be deleted. See the log.",
                          "Not everything was deleted");
   }

   private static async Task<bool> ConfirmDeleteAllAsync() =>
            await new Wpf.Ui.Controls.MessageBox
                  {
                           Title             = "Delete all Entrée data?",
                           Content           = "This removes Entrée's scheduled tasks, settings, saved domains and logs " +
                                               "from this PC, then closes Entrée. Reports in your results folder are kept.",
                           PrimaryButtonText = "Delete",
                           CloseButtonText   = "Cancel"
                  }.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary;

   public async Task CheckForUpdatesAsync(bool quiet)
   {
      if (!_updates.IsInstalled || IsUpdating) return;
      IsUpdating   = true;
      UpdateStatus = "Checking…";
      try
      {
         _availableVersion = await _updates.CheckAsync();
         UpdateStatus      = _availableVersion is null ? $"You have the latest version ({AppVersion})" : "A new version is ready";
         OnPropertyChanged(nameof(UpdateAction));
         if (_availableVersion is not null && quiet)
            _notifications.Show(InfoBarSeverity.Informational,
                                $"Entrée {_availableVersion} can be installed from Settings, under About.",
                                "Update available");
      }
      catch (Exception ex)
      {
         Log.Warning(ex, "Update check failed");
         UpdateStatus = "Could not reach GitHub";
         if (!quiet) _notifications.Show(InfoBarSeverity.Warning, ex.Message, "Update check failed");
      }
      finally
      {
         IsUpdating = false;
      }
   }

   private async Task UpdateAsync()
   {
      if (!_updates.IsInstalled)
      {
         Process.Start(new ProcessStartInfo(UpdateService.ReleasesUrl) { UseShellExecute = true })?.Dispose();
         return;
      }

      if (_availableVersion is null)
      {
         await CheckForUpdatesAsync(false);
         return;
      }

      if (_searchService.IsSearchRunning)
      {
         _notifications.Show(InfoBarSeverity.Warning, "Wait for the running searches to finish, then install.",
                             "Searches are running");
         return;
      }

      IsUpdating = true;
      try
      {
         await _updates.DownloadAsync(percent => UpdateStatus = $"Downloading… {percent}%");
         if (_searchService.IsSearchRunning)
         {
            UpdateStatus = $"Version {_availableVersion} is ready to install";
            _notifications.Show(InfoBarSeverity.Warning, "Wait for the running searches to finish, then install.",
                                "Searches are running");
            return;
         }

         _updates.InstallOnExit();
         Exit();
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Update to {Version} failed", _availableVersion);
         UpdateStatus = "The update could not be installed";
         _notifications.Show(InfoBarSeverity.Error, ex.Message, "Update failed");
      }
      finally
      {
         IsUpdating = false;
      }
   }

   private void SelectPath(bool isLog)
   {
      OpenFolderDialog dialog = new();
      if (dialog.ShowDialog() != true) return;

      if (!isLog)
      {
         ResultPath = dialog.FolderName;
         return;
      }

      LogPath = dialog.FolderName;
      _notifications.Show(InfoBarSeverity.Informational, "Entrée writes its log there after the next start.",
                          "Log folder saved");
   }

   private async Task ShowThirdPartyLicenseAsync() => await _licenseService.ShowThirdPartyLicenseAsync();
   private async Task ShowLicenseAsync()           => await _licenseService.ShowLicenseAsync();

   private async Task ValidateDomainAsync()
   {
      if (IsValidating) return;
      if (string.IsNullOrWhiteSpace(DomainPath))
      {
         ShowValidationResult("Validation Error", "Please enter a domain or server path", InfoBarSeverity.Error);
         return;
      }

      IsValidating            = true;

      try
      {
         var normalizePath = PathHelper.ValidateAndNormalizePath(DomainPath);
         if (string.IsNullOrWhiteSpace(normalizePath))
         {
            ShowValidationResult("Invalid Path", "The domain path format is not valid", InfoBarSeverity.Error);
            return;
         }

         var credential = new SecureCredential
                          { DomainPath = normalizePath, Username = DomainUsername, Password = DomainPassword };
         (var isValid, var errorMessage) = await Task.Run(() => credential.Validate());

         if (isValid)
         {
            ShowValidationResult("Validation Successful", "Connected.", InfoBarSeverity.Success);
            DomainPath = normalizePath;
         }
         else
         {
            ShowValidationResult("Validation Failed", errorMessage, InfoBarSeverity.Error);
         }
      }
      catch (Exception ex)
      {
         ShowValidationResult("Validation Error", $"An error occurred: {ex.Message}", InfoBarSeverity.Error);
      }
      finally
      {
         IsValidating = false;
      }
   }

   private async Task AddDomainAsync()
   {
      if (IsValidating) return;
      if (string.IsNullOrWhiteSpace(DomainPath))
      {
         ShowValidationResult("Error", "Please enter a domain or server path", InfoBarSeverity.Error);
         return;
      }

      IsValidating            = true;

      try
      {
         var normalizedPath = PathHelper.ValidateAndNormalizePath(DomainPath);
         if (string.IsNullOrWhiteSpace(normalizedPath))
         {
            ShowValidationResult("Invalid Path", "The domain path format is invalid", InfoBarSeverity.Error);
            return;
         }

         if (Domains.Any(domain => domain.DomainPath != null &&
                                   domain.DomainPath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)))
         {
            ShowValidationResult("Duplicate", "This domain is already in the list", InfoBarSeverity.Warning);
            return;
         }

         var credential = new SecureCredential
                          { DomainPath = normalizedPath, Username = DomainUsername, Password = DomainPassword };
         (var isValid, var errorMessage) = await Task.Run(() => credential.Validate());

         if (isValid)
         {
            if (!await _credentialService.AddCredentialAsync(credential))
            {
               ShowValidationResult("Error", "The domain could not be saved", InfoBarSeverity.Error);
               return;
            }

            DomainPath     = string.Empty;
            DomainUsername = string.Empty;
            DomainPassword = string.Empty;

            ShowValidationResult("Domain Added", "The domain successfully connected and added ",
                                 InfoBarSeverity.Success);
         }
         else
         {
            ShowValidationResult("Validation Failed", errorMessage, InfoBarSeverity.Error);
         }
      }
      catch (Exception ex)
      {
         ShowValidationResult("Error", $"An error occurred: {ex.Message}", InfoBarSeverity.Error);
      }
      finally
      {
         IsValidating = false;
      }
   }

   private async Task RemoveDomainAsync(object parameter)
   {
      if (parameter is SecureCredential domain)
         await _credentialService.RemoveCredentialAsync(domain);
   }

   private void ShowValidationResult(string title, string message, InfoBarSeverity severity) =>
            _notifications.Show(severity, message, title);

   internal static string FormatVersion(string? informationalVersion) =>
            informationalVersion?.Split('+') switch
            {
                     [var version, var commit] => $"{version} ({commit[..Math.Min(7, commit.Length)]})",
                     [var version]             => version,
                     _                         => "Unknown"
            };
}
