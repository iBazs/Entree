// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using Serilog;
using Entree.Core.API;
using Entree.Core.BaseClass;
using Entree.Core.Handler;
using Entree.Core.Helpers;
using Entree.MVVM.Model;
using Entree.MVVM.ViewModel;

namespace Entree.Core.Controller;

internal sealed class SearchController : IDisposable
{
   private readonly SearchConfigViewModel _config;
   private readonly ParallelOptions       _pOptions;
   private readonly FolderTraversalApi    _search;
   private readonly SecurityApi           _security;
   private readonly Stopwatch             _timer = new();

   internal SearchController(SearchConfigViewModel searchConfig, ParallelOptions pOptions)
   {
      _config   = searchConfig ?? throw new ArgumentNullException(nameof(searchConfig));
      _pOptions = pOptions ?? throw new ArgumentNullException(nameof(pOptions));
      _security = new SecurityApi(_config.Domains, _pOptions);
      _search   = new FolderTraversalApi(_config, _security, _pOptions);
   }

   public void Dispose() => _security.Dispose();

   public void Start()
   {
      if (_config.IsRunning)
      {
         Log.Warning("Search already in progress");
         return;
      }

      _config.IsRunning = true;
      Log.Information("Search Start on: {searchPath}", _config.SearchDirectory);

      var searchTime = Stopwatch.StartNew();
      _timer.Start();

      try
      {
         ExecuteSearchPipeline();
      }
      catch (OperationCanceledException)
      {
         OnCancel();
      }
      catch (Exception e)
      {
         OnError(e);
      }
      finally
      {
         OnComplete(searchTime);
      }
   }

   private void ExecuteSearchPipeline()
   {
      var results = PerformSearch();
      if (results.Length == 0)
      {
         _config.IsDone = true;
         return;
      }

      _config.Progress.Phase = "Resolving accounts";
      Log.Information("Resolve SIDs from search results");
      var accounts = ResolveSidsToAccounts(_security.GetSecurityIdentifiers());

      var groups = new ConcurrentBag<AccountInfo[]>();
      if (_config.ShowGroups)
      {
         _config.Progress.Phase = "Reading groups";
         Log.Information("Retrieve all Groups and their members");
         groups = RetrieveGroupMembers(accounts);
      }

      _config.Progress.Phase = "Writing report";
      Log.Information("Building File");
      ExportToFile(_config, results, groups.OrderBy(group => group.Length).Reverse().ToList(), accounts, _pOptions);
      _config.IsDone = true;
   }

   private FileSystemStruct[] PerformSearch()
   {
      _config.Progress.Phase = "Reading folders";
      var results = _search.Start().ToArray();
      _timer.Stop();

      Array.Sort(results, (x, y) => string.CompareOrdinal(x.Path, y.Path));
      results = MergeRows(results);

      Log.Information("Search Completed.");
      if (results.Length == 0)
         Log.Warning("Search was empty.");
      else
         Log.Debug("Time: {elapsedTime} . Found {count} Entries.", _timer.Elapsed, results.Length);

      return results;
   }

   internal static FileSystemStruct[] MergeRows(FileSystemStruct[] sorted)
   {
      if (!sorted.Skip(1).Where((row, i) => row.Path == sorted[i].Path).Any()) return sorted;
      var merged = new List<FileSystemStruct>(sorted.Length);
      foreach (var row in sorted)
         if (merged.Count > 0 && merged[^1].Path == row.Path) merged[^1] = Combine(merged[^1], row);
         else merged.Add(row);
      return merged.ToArray();
   }

   private static FileSystemStruct Combine(FileSystemStruct first, FileSystemStruct second)
   {
      var (row, other) = first.HasError && !second.HasError ? (second, first) : (first, second);
      return row with { Security = row.Security with { Error = row.Error ?? other.Error } };
   }

   private ConcurrentDictionary<SecurityIdentifier, List<AccountInfo>> ResolveSidsToAccounts(
            IEnumerable<SecurityIdentifier> sids)
   {
      var accountDict = new ConcurrentDictionary<SecurityIdentifier, List<AccountInfo>>();

      try
      {
         sids.AsParallel()
             .WithDegreeOfParallelism(_pOptions.MaxDegreeOfParallelism)
             .WithCancellation(_pOptions.CancellationToken)
             .ForAll(sid =>
              {
                 try
                 {
                    accountDict.TryAdd(sid, _security.GetAccountInfos(sid));
                 }
                 catch (Exception e) when (e is not OperationCanceledException)
                 {
                    Log.Error(e, "Cannot resolve SID {Sid}: {Message}", sid, e.Message);
                    accountDict.TryAdd(sid, [new AccountInfo(sid, error: e.Message)]);
                 }
              });
      }
      catch (AggregateException ae) when (ae.InnerException is OperationCanceledException oce)
      {
         throw oce;
      }

      return accountDict;
   }

   private ConcurrentBag<AccountInfo[]> RetrieveGroupMembers(
            ConcurrentDictionary<SecurityIdentifier, List<AccountInfo>> accountDict)
   {
      var groups = new ConcurrentBag<AccountInfo[]>();

      var getGroups = accountDict.Values
                                 .AsParallel()
                                 .WithDegreeOfParallelism(_pOptions.MaxDegreeOfParallelism)
                                 .WithCancellation(_pOptions.CancellationToken)
                                 .SelectMany(accountInfoArray => accountInfoArray)
                                 .Where(accountInfo => accountInfo.Type == AccountType.Group);

      var getMembers = getGroups
                      .SelectMany(accountInfo => _security.GetGroupMembers(accountInfo))
                      .Where(accountInfoArray => accountInfoArray.Length > 0);

      getMembers.ForAll(group => groups.Add(group));

      return groups;
   }

   private void OnCancel()
   {
      _config.IsCancelled = true;
      _timer.Stop();
      Log.Debug("Search was cancelled");
   }

   private void OnError(Exception e)
   {
      _config.IsError = true;
      _timer.Stop();
      Log.Error(e, "Error happened while search: {Message}", e.Message);
   }

   private void OnComplete(Stopwatch searchTime)
   {
      searchTime.Stop();
      Log.Information("Search time: {time}", searchTime.Elapsed);
      _config.IsRunning   = false;
      _config.IsScheduled = false;
   }

   private static void ExportToFile(
            SearchConfigViewModel searchConfig,
            IReadOnlyList<FileSystemStruct> resultList,
            List<AccountInfo[]> groupList,
            ConcurrentDictionary<SecurityIdentifier, List<AccountInfo>> accountsDictionary,
            ParallelOptions pOptions)
   {
      if (resultList.Count == 0) return;

      var exportParams = CreateExportParameters(searchConfig);
      try
      {
         if (exportParams.IsXlsx)
            new ExcelHandler(searchConfig, resultList, groupList, accountsDictionary)
                     .Export(exportParams.FullPath, pOptions.CancellationToken);
         else
            new CsvHandler(searchConfig, resultList, groupList, accountsDictionary)
                     .Export(exportParams.FullPath, pOptions.CancellationToken);
      }
      catch
      {
         PathHelper.TryDelete(exportParams.FullPath);
         throw;
      }

      searchConfig.FileName = exportParams.FullPath;
      if (searchConfig.OpenResult)
         PathHelper.OpenFile(exportParams.FullPath);
   }

   private static (string FullPath, bool IsXlsx) CreateExportParameters(SearchConfigViewModel searchConfig)
   {
      var isXlsx = searchConfig.ExportAs == ExportAs.Xlsx;
      return (PathHelper.ReserveUniqueFile(PathHelper.ReportName(searchConfig.SearchDirectory), isXlsx ? ".xlsx" : ".zip",
                                           PathHelper.EnsureDirectoryExists(searchConfig.SaveDirectory)), isXlsx);
   }
}

