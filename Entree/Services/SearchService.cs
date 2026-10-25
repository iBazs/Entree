// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using Serilog;
using Entree.Core.BaseClass;
using Entree.Core.Controller;
using Entree.Core.Helpers;
using Entree.MVVM.Model;
using Entree.MVVM.ViewModel;

namespace Entree.Services;

public interface ISearchService : INotifyPropertyChanged
{
   ObservableCollection<SearchConfigViewModel> SearchConfigs   { get; }
   bool                                        IsSearchRunning { get; }
   void                                        Stop();
   void                                        Stop(Guid id);
   Task<bool>                                  Search(IEnumerable<SearchConfigViewModel> searchConfigs);
   Task                                        WhenIdle();
}

public sealed class SearchService(IAppSettings appSettings, ICredentialService credentialService)
         : ObservableObject, ISearchService
{
   private readonly object                     _gate         = new();
   private readonly Dictionary<string, Queued> _pendingByKey = new(StringComparer.Ordinal);
   private readonly Queue<Queued>              _queue        = new();
   private          TaskCompletionSource       _idle         = Idle();
   private          bool                       _running;

   private ConcurrentDictionary<Guid, CancellationTokenSource> CancelConfigs { get; } = [];
   public  ObservableCollection<SearchConfigViewModel>         SearchConfigs { get; } = [];

   public bool IsSearchRunning
   {
      get
      {
         lock (_gate) return _running;
      }
   }

   public void Stop()
   {
      foreach (var id in CancelConfigs.Keys) Stop(id);
   }

   public void Stop(Guid id)
   {
      if (!CancelConfigs.TryRemove(id, out var tokenSource)) return;
      TryCancel(tokenSource);
      OnUi(() =>
      {
         if (SearchConfigs.FirstOrDefault(x => x.Id == id) is { } config) config.IsCancelled = true;
      });
   }

   public Task<bool> Search(IEnumerable<SearchConfigViewModel> searchConfigs)
   {
      var waits = new List<Task<bool>>();
      var added = new List<Queued>();
      bool start;
      lock (_gate)
      {
         foreach (var source in searchConfigs)
         {
            var key = QueueKey(source);
            if (_pendingByKey.TryGetValue(key, out var waiting) && !waiting.Cancel.IsCancellationRequested)
            {
               Log.Information("{Directory} is already queued with the same options; sharing its result",
                               source.SearchDirectory);
               waits.Add(waiting.Done.Task);
               continue;
            }

            var config = source.Clone();
            var queued = new Queued(config, new CancellationTokenSource(),
                                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
                                    key);
            _queue.Enqueue(queued);
            _pendingByKey[key]       = queued;
            CancelConfigs[config.Id] = queued.Cancel;
            added.Add(queued);
            waits.Add(queued.Done.Task);
         }

         start = !_running && _queue.Count > 0;
         if (start)
         {
            _running = true;
            _idle    = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
         }
      }

      OnUi(() =>
      {
         foreach (var queued in added) SearchConfigs.Add(queued.Config);
      });
      if (start)
      {
         OnPropertyChanged(nameof(IsSearchRunning));
         _ = Task.Run(ProcessQueue);
      }

      return AllSucceeded(waits);
   }

   public Task WhenIdle()
   {
      lock (_gate) return _idle.Task;
   }

   internal static string QueueKey(SearchConfigViewModel config) =>
            string.Join('|', Path.TrimEndingDirectorySeparator(config.SearchDirectory ?? string.Empty).ToUpperInvariant(),
                        Path.TrimEndingDirectorySeparator(config.SaveDirectory ?? string.Empty).ToUpperInvariant(),
                        config.SearchFor, config.DepthLevel, config.IsLocalSearch, config.Inherited, config.ShowOwner,
                        config.ShowGroups, config.ShowMatrix, config.StringExportAs, config.OpenResult,
                        string.Join(',', config.ExcludedAccounts.Order(StringComparer.OrdinalIgnoreCase)));

   private static TaskCompletionSource Idle()
   {
      var idle = new TaskCompletionSource();
      idle.SetResult();
      return idle;
   }

   private static async Task<bool> AllSucceeded(List<Task<bool>> waits) => (await Task.WhenAll(waits)).All(ok => ok);

   private void ProcessQueue()
   {
      while (true)
      {
         var (next, idle) = Dequeue();
         if (next is not null)
         {
            next.Done.TrySetResult(Run(next.Config, next.Cancel));
            continue;
         }

         OnPropertyChanged(nameof(IsSearchRunning));
         _ = MemoryRelease.AfterLargeOperation();
         idle?.TrySetResult();
         return;
      }
   }

   private (Queued? Next, TaskCompletionSource? Idle) Dequeue()
   {
      lock (_gate)
      {
         if (_queue.TryDequeue(out var next))
         {
            if (_pendingByKey.GetValueOrDefault(next.Key) == next) _pendingByKey.Remove(next.Key);
            return (next, null);
         }

         _running = false;
         return (null, _idle);
      }
   }

   private bool Run(SearchConfigViewModel searchConfig, CancellationTokenSource cTokenSource)
   {
      try
      {
         cTokenSource.Token.ThrowIfCancellationRequested();
         var parallelOptions = new ParallelOptions
                               {
                                        CancellationToken      = cTokenSource.Token,
                                        MaxDegreeOfParallelism = Parallelism.Degree(appSettings.ThreadLimit)
                               };

         if (!searchConfig.IsLocalSearch)
            foreach (var credential in OnUi(() => credentialService.GetObservableCredentials().ToList()))
            {
               var principalTask = Task.Run(credential.CreatePrincipalContext);
               var completed     = principalTask.Wait(TimeSpan.FromSeconds(2), cTokenSource.Token);
               var result        = completed ? principalTask.Result : null;
               if (!completed)
                  _ = principalTask.ContinueWith(t => t.Result?.Dispose(), TaskContinuationOptions.OnlyOnRanToCompletion);
               if (result is null)
               {
                  Log.Error("Could not use {Domain} for translation.", credential.DomainPath);
                  continue;
               }

               OnUi(() =>
               {
                  searchConfig.Domains.Add(result);
                  searchConfig.DomainNames.Add(credential.DomainPath ?? string.Empty);
               });
            }

         using var search = new SearchController(searchConfig, parallelOptions);
         search.Start();
         CancelConfigs.TryRemove(searchConfig.Id, out _);
      }
      catch (OperationCanceledException)
      {
         searchConfig.IsCancelled = true;
      }
      catch (ObjectDisposedException)
      {
         searchConfig.IsCancelled = true;
      }
      catch (OutOfMemoryException oomEx)
      {
         searchConfig.IsError = true;
         Log.Error("Out of memory during search. Try limiting search depth or using smaller directories: {Message}",
                   oomEx.Message);
      }
      catch (Exception ex)
      {
         searchConfig.IsError = true;
         Log.Error(ex, "Unexpected error while searching {Directory}: {Message}", searchConfig.SearchDirectory,
                   ex.Message);
      }
      finally
      {
         searchConfig.TimerStop();
         CancelConfigs.TryRemove(searchConfig.Id, out _);
         cTokenSource.Dispose();
         foreach (var domain in searchConfig.Domains) domain.Dispose();
         OnUi(searchConfig.Domains.Clear);
      }

      return !searchConfig.IsCancelled && !searchConfig.IsError;
   }

   private static void TryCancel(CancellationTokenSource tokenSource)
   {
      try
      {
         tokenSource.Cancel();
      }
      catch (ObjectDisposedException)
      {
      }
   }

   private static void OnUi(Action action)
   {
      var dispatcher = Application.Current?.Dispatcher;
      if (dispatcher is null || dispatcher.CheckAccess()) action();
      else dispatcher.Invoke(action);
   }

   private static T OnUi<T>(Func<T> function)
   {
      var dispatcher = Application.Current?.Dispatcher;
      return dispatcher is null || dispatcher.CheckAccess() ? function() : dispatcher.Invoke(function);
   }

   private sealed record Queued(SearchConfigViewModel      Config,
                                CancellationTokenSource    Cancel,
                                TaskCompletionSource<bool> Done,
                                string                     Key);
}
