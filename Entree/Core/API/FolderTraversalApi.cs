// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using Serilog;
using Entree.Core.BaseClass;
using Entree.Core.Helpers;
using Entree.MVVM.Model;
using Entree.MVVM.ViewModel;

namespace Entree.Core.API;

public class FolderTraversalApi(
         SearchConfigViewModel searchConfig,
         SecurityApi securityApi,
         ParallelOptions parallelOptions)
{
   public IEnumerable<FileSystemStruct> Start() => new FileEnumerable(searchConfig, securityApi, parallelOptions);

   private class FileEnumerable(
            SearchConfigViewModel searchConfig,
            SecurityApi securityApi,
            ParallelOptions parallelOptions)
            : IEnumerable<FileSystemStruct>
   {
      public IEnumerator<FileSystemStruct> GetEnumerator() =>
               new ParallelFileEnumerator(searchConfig, securityApi, parallelOptions);

      IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
   }

   [SuppressUnmanagedCodeSecurity]
   private class ParallelFileEnumerator : IEnumerator<FileSystemStruct>
   {
      private readonly int            _maxDegreeOfParallelism;
      private readonly CountdownEvent _producersCompletionEvent;
      private          int            _activeProducerCount;
      private          int            _pendingDirectories = 1;
      private const    int            ErrorFileNotFound = 2;
      private const    int            ErrorNoMoreFiles  = 18;
      private const    int            NameSurrogateTag  = 0x20000000;
      private static readonly long    MaxFileTime       = DateTime.MaxValue.ToFileTimeUtc();
      private          bool           _disposed;
      private volatile bool           _enumerateRunning = true;

      public ParallelFileEnumerator(SearchConfigViewModel searchConfig, SecurityApi securityApi,
                                    ParallelOptions parallelOptions)
      {
         if (!Directory.Exists(searchConfig.SearchDirectory))
            throw new
                     DirectoryNotFoundException($"The search directory '{searchConfig.SearchDirectory}' does not exist or is not accessible.");
         SecurityApi               = securityApi;
         CToken                    = parallelOptions.CancellationToken;
         _maxDegreeOfParallelism   = parallelOptions.MaxDegreeOfParallelism;
         _activeProducerCount      = _maxDegreeOfParallelism;
         _producersCompletionEvent = new CountdownEvent(_maxDegreeOfParallelism);

         Progress      = searchConfig.Progress;
         SearchPath    = Path.TrimEndingDirectorySeparator(Path.GetFullPath(searchConfig.SearchDirectory));
         SearchPattern = "*";
         SearchFor     = searchConfig.SearchFor;

         DepthLevel = searchConfig.DepthLevel;
         ShowOwner  = searchConfig.ShowOwner;
         Inherited  = searchConfig.Inherited;

         DirectoryQueue = new ConcurrentQueue<(string path, int depth)>([(SearchPath, 0)]);
         ResultQueue    = [];
         var searchDirectoryInfo = new DirectoryInfo(SearchPath);
         ResultQueue.Add(SecurityApi.GetFullSecurity(SearchPath,
                                                     FileAttributes.Directory,
                                                     searchDirectoryInfo.LastWriteTime, ShowOwner, Inherited));
         StartProducerTasks();
      }

      private BlockingCollection<FileSystemStruct>      ResultQueue     { get; }
      private ConcurrentQueue<(string path, int depth)> DirectoryQueue  { get; }
      private AutoResetEvent                            WorkAvailable   { get; } = new(false);
      private ManualResetEventSlim                      CompletionEvent { get; } = new(false);

      private string?   SearchPath    { get; }
      private SearchFor SearchFor     { get; }
      private string    SearchPattern { get; }

      private int  DepthLevel { get; }
      private bool Inherited  { get; }
      private bool ShowOwner  { get; }

      private SecurityApi       SecurityApi { get; }
      private SearchProgress    Progress    { get; }
      private CancellationToken CToken      { get; }

      public FileSystemStruct Current { get; private set; }
      object IEnumerator.     Current => Current;

      public bool MoveNext()
      {
         while (_enumerateRunning || ResultQueue.Count > 0 || _activeProducerCount > 0)
         {
            if (!ResultQueue.TryTake(out var item, 1000, CToken)) continue;
            if (item.HasError) Log.Error("{ItemPath}: {ItemError}", item.Path, item.Error);
            Current = item;
            Progress.ItemFound();
            return true;
         }

         Log.Debug("No more items in the queue.");
         return false;
      }

      public void Reset()
      {
         throw new NotSupportedException(
                                         "ParallelFileEnumerator does not support Reset due to the parallel nature of enumeration.");
      }

      public void Dispose()
      {
         if (_disposed) return;
         _disposed         = true;
         _enumerateRunning = false;
         WorkAvailable.Set();
         var producersDone = _producersCompletionEvent.CurrentCount is 0 ||
                             _producersCompletionEvent.Wait(TimeSpan.FromSeconds(3));
         ResultQueue.CompleteAdding();
         if (!producersDone) return;
         _producersCompletionEvent.Dispose();
         CompletionEvent.Dispose();
         WorkAvailable.Dispose();
         ResultQueue.Dispose();
      }

      private const int FindExInfoBasic       = 1;
      private const int FindExSearchNameMatch = 0;
      private const int FindFirstExLargeFetch = 2;

      [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindFirstFileExW", SetLastError = true)]
      private static extern SafeFindHandle FindFirstFileEx(string fileName, int infoLevel, out Win32FindData data,
                                                           int searchOp, IntPtr searchFilter, int additionalFlags);

      [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
      private static extern bool FindNextFile(SafeFindHandle hndFindFile, out Win32FindData lpFindFileData);

      private void ProducerWork()
      {
         try
         {
            while (!CToken.IsCancellationRequested && _enumerateRunning)
               if (DirectoryQueue.TryDequeue(out var current))
                  try
                  {
                     if (DepthLevel == -1 || current.depth < DepthLevel)
                        ProcessDirectory(current.path, current.depth);
                  }
                  finally
                  {
                     if (Interlocked.Decrement(ref _pendingDirectories) == 0)
                     {
                        _enumerateRunning = false;
                        WorkAvailable.Set();
                     }
                  }
               else
                  WorkAvailable.WaitOne(100);
         }
         finally
         {
            Log.Debug("Finished Worker.");
            _producersCompletionEvent.Signal();

            if (Interlocked.Decrement(ref _activeProducerCount) == 0)
               lock (ResultQueue)
               {
                  _enumerateRunning = false;
                  CompletionEvent.Set();
               }
         }
      }

      private void StartProducerTasks()
      {
         for (var i = 0; i < _maxDegreeOfParallelism; i++)
            Task.Factory.StartNew(ProducerWork, CToken, TaskCreationOptions.LongRunning,
                                  TaskScheduler.Default);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static bool IsToLeftOut(string fileName)
      {
         return fileName switch
                {
                         "." or ".." or "Thumbs.db" => true,
                         _                          => false
                };
      }

      private void ProcessDirectory(string path, int depth)
      {
         Progress.EnterFolder(path);
         try
         {
            using var hFind = FindFirstFileEx(PathHelper.ExtendedLengthPath(ChildPath(path, SearchPattern)), FindExInfoBasic,
                                              out var findData, FindExSearchNameMatch, IntPtr.Zero,
                                              FindFirstExLargeFetch);

            if (hFind.IsInvalid)
            {
               var error = Marshal.GetLastWin32Error();
               if (error is ErrorFileNotFound or ErrorNoMoreFiles) return;
               ResultQueue.Add(ErrorRow(path, new Win32Exception(error).Message), CToken);
               return;
            }

            do
            {
               if (CToken.IsCancellationRequested) return;
               if (!IsToLeftOut(findData.cFileName)) ProcessEntry(path, depth, findData);
            } while (FindNextFile(hFind, out findData));

            var listingError = Marshal.GetLastWin32Error();
            if (listingError != ErrorNoMoreFiles)
               ResultQueue.Add(ErrorRow(path, new Win32Exception(listingError).Message), CToken);
         }
         catch (OperationCanceledException)
         {
         }
         catch (Exception ex)
         {
            Log.Error("Error processing directory {Path}: {ExMessage}", path, ex.Message);
            ResultQueue.Add(ErrorRow(path, $"Processing error: {ex.Message}"), CToken);
         }
      }

      private void ProcessEntry(string path, int depth, Win32FindData findData)
      {
         var fullPath    = ChildPath(path, findData.cFileName);
         var isDirectory = findData.dwFileAttributes.HasFlag(FileAttributes.Directory);
         try
         {
            if (isDirectory && !IsNameSurrogate(findData))
            {
               Interlocked.Increment(ref _pendingDirectories);
               DirectoryQueue.Enqueue((fullPath, depth + 1));
               WorkAvailable.Set();
            }

            if (SearchFor == (isDirectory ? SearchFor.Files : SearchFor.Folders)) return;
            ResultQueue.Add(SecurityApi.GetFullSecurity(fullPath, findData.dwFileAttributes,
                                                        ConvertDateTime(findData.ftLastWriteTime_dwHighDateTime,
                                                                        findData.ftLastWriteTime_dwLowDateTime),
                                                        ShowOwner, Inherited), CToken);
         }
         catch (Exception ex) when (ex is not OperationCanceledException)
         {
            Log.Error("Error processing {Path}: {ExMessage}", fullPath, ex.Message);
            ResultQueue.Add(ErrorRow(fullPath, $"Processing error: {ex.Message}"), CToken);
         }
      }

      private static bool IsNameSurrogate(Win32FindData findData) =>
               findData.dwFileAttributes.HasFlag(FileAttributes.ReparsePoint) &&
               (findData.dwReserved0 & NameSurrogateTag) != 0;

      private static FileSystemStruct ErrorRow(string path, string error) =>
               new() { Path = path, Security = SecurityInfo.Failed(error) };

      private static string ChildPath(string path, string name) =>
               Path.EndsInDirectorySeparator(path) ? string.Concat(path, name) : string.Concat(path, "\\", name);

      private static long CombineHighLowInts(uint high, uint low) => ((long)high << 0x20) | low;

      private static DateTime ConvertDateTime(uint high, uint low)
      {
         var fileTime = CombineHighLowInts(high, low);
         return fileTime is < 0 || fileTime > MaxFileTime ? DateTime.MinValue : DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
      }
   }
}