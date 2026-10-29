// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Windows;
using Serilog;
using Entree.Core.API;
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
   private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

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

         IReadOnlyList<DomainContext> domains =
         [
                  ..ShareServer(searchConfig.SearchDirectory, cTokenSource.Token),
                  ..Connect(DomainRouting.Plan(Trusts(cTokenSource.Token),
                                               OnUi(() => credentialService.GetObservableCredentials().ToList()),
                                               searchConfig.IsLocalSearch),
                            cTokenSource.Token)
         ];
         OnUi(() =>
         {
            foreach (var domain in domains) searchConfig.Domains.Add(domain);
         });

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
         foreach (var domain in searchConfig.Domains) DomainRouting.Directory(domain)?.Dispose();
         OnUi(searchConfig.Domains.Clear);
      }

      return !searchConfig.IsCancelled && !searchConfig.IsError;
   }

   private static IReadOnlyList<DomainContext> Connect(IReadOnlyList<DomainSource> sources, CancellationToken token) =>
            sources.AsParallel().AsOrdered().WithCancellation(token).SelectMany(source => Open(source, token)).ToList();

   private static IReadOnlyList<DomainContext> Open(DomainSource source, CancellationToken token)
   {
      var name = source.Credential.DomainPath ?? string.Empty;
      switch (WithTimeout(() => Connected(source.Credential.CreatePrincipalContext()), token))
      {
         case Outcome<PrincipalContext>.Done done:
            return [new DomainContext.Domain(name, done.Value, source.Sid, source.NetBiosName)];
         case Outcome<PrincipalContext>.Failed failed:
            Log.Error("Could not use {Domain} for translation: {Reason}", name, failed.Reason);
            break;
      }

      return [];
   }

   private static IReadOnlyList<DomainContext> ShareServer(string? path, CancellationToken token)
   {
      if (DomainRouting.ShareServer(DomainLookup.RemotePath(path)) is not { } server) return [];
      switch (WithTimeout(() => OpenMachine(server), token))
      {
         case Outcome<PrincipalContext>.Done done:
            return [new DomainContext.Server(server, done.Value)];
         case Outcome<PrincipalContext>.Failed failed:
            Log.Warning("Cannot read the local accounts of {Server}, only their names are resolved: {Reason}", server,
                        failed.Reason);
            break;
      }

      return [new DomainContext.Server(server, null)];
   }

   private static Outcome<PrincipalContext> Connected(PrincipalContext? context) =>
            context is null
                     ? new Outcome<PrincipalContext>.Failed("The domain did not accept the connection")
                     : new Outcome<PrincipalContext>.Done(context);

   private static Outcome<PrincipalContext> OpenMachine(string server)
   {
      try
      {
         return new Outcome<PrincipalContext>.Done(new PrincipalContext(ContextType.Machine, server));
      }
      catch (Exception ex)
      {
         return new Outcome<PrincipalContext>.Failed(ex.Message);
      }
   }

   private static IReadOnlyList<TrustedDomain> Trusts(CancellationToken token)
   {
      switch (WithTimeout(DomainLookup.Trusts, token))
      {
         case Outcome<IReadOnlyList<TrustedDomain>>.Done done:
            return done.Value;
         case Outcome<IReadOnlyList<TrustedDomain>>.Failed failed:
            Log.Information("No domain trusts are used, only domains from Settings: {Reason}", failed.Reason);
            break;
      }

      return [];
   }

   private static Outcome<T> WithTimeout<T>(Func<Outcome<T>> work, CancellationToken token)
   {
      var task = Task.Run(work, token);
      try
      {
         if (task.Wait(ConnectTimeout, token)) return task.Result;
      }
      catch (AggregateException ex)
      {
         return new Outcome<T>.Failed(ex.InnerException?.Message ?? ex.Message);
      }

      _ = task.ContinueWith(t =>
      {
         if (t.Result is Outcome<T>.Done { Value: IDisposable late }) late.Dispose();
      }, TaskContinuationOptions.OnlyOnRanToCompletion);
      return new Outcome<T>.Failed($"No answer within {ConnectTimeout.TotalSeconds:0} seconds");
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
