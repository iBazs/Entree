// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.ObjectModel;
using System.ComponentModel;
using Serilog.Events;
using Entree.Core.BaseClass;
using Entree.MVVM.Model;
using Entree.MVVM.ViewModel;
using Entree.Services;

namespace Entree.Tests.Fakes;

internal sealed class FakeSearchService : ISearchService
{
   public event PropertyChangedEventHandler? PropertyChanged;

   public ObservableCollection<SearchConfigViewModel> SearchConfigs   { get; } = [];
   public bool                                        IsSearchRunning { get; set; }

   public void Stop() { }

   public void Stop(Guid id) { }

   public Task<bool> Search(IEnumerable<SearchConfigViewModel> searchConfigs)
   {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSearchRunning)));
      return Task.FromResult(true);
   }

   public Task WhenIdle() => Task.CompletedTask;
}

internal sealed class FakeUpdateService : IUpdateService
{
   public bool    IsInstalled { get; set; }
   public string? Available   { get; set; }
   public int     Installs    { get; private set; }

   public Task<string?> CheckAsync() => Task.FromResult(Available);

   public Task DownloadAndRestartAsync(Action<int> progress)
   {
      Installs++;
      return Task.CompletedTask;
   }
}

internal sealed class FakeUserDataService : IUserDataService
{
   public bool Succeeds { get; init; } = true;
   public int  Deletes  { get; private set; }

   public Task<bool> DeleteAllAsync()
   {
      Deletes++;
      return Task.FromResult(Succeeds);
   }
}

internal sealed class FakeNavigationService : INavigationService
{
   public event PropertyChangedEventHandler? PropertyChanged;

   public ViewModel CurrentView { get; private set; } = null!;

   public void NavigateTo(Type viewModelType) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentView)));
}

internal sealed class FakeCompareService : ICompareService
{
   public Task<List<ComparedFileSystemStructResult>> CompareFilesAsync(string originalFilePath, string compareFilePath,
                                                                       CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<ComparedFileSystemStructResult>());

   public Task<string> ExportResultsAsync(bool exportAsXlsx, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

   public void Clear() { }
}

internal sealed class FakeAppSettings : IAppSettings
{
   public string        AppLogPath    { get; set; } = @"C:\Users\Admin\AppData\Local\Entree\Log";
   public string        AppResultPath { get; set; } = @"C:\Users\Admin\Documents\Entree\Results";
   public int           ThreadLimit   { get; set; } = 8;
   public LogEventLevel LogLevel      => LogEventLevel.Information;
   public string        Theme         { get; set; } = "System";
}
