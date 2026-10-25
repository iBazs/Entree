// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Serilog.Core;
using Serilog.Events;

namespace Entree.Services;

public class MemorySink(int maxEvent = 1000) : ILogEventSink
{
   private readonly ConcurrentQueue<LogEvent> _pending = new();
   private          int                       _flushScheduled;

   public ObservableCollection<LogEvent> LogEvents { get; } = [];

   public void Emit(LogEvent logEvent)
   {
      if (Application.Current?.Dispatcher is not { } dispatcher) return;
      _pending.Enqueue(logEvent);
      while (_pending.Count > maxEvent) _pending.TryDequeue(out _);
      if (Interlocked.Exchange(ref _flushScheduled, 1) == 1) return;
      try
      {
         dispatcher.BeginInvoke(Flush, DispatcherPriority.Background);
      }
      catch (OperationCanceledException)
      {
         Interlocked.Exchange(ref _flushScheduled, 0);
      }
   }

   private void Flush()
   {
      Interlocked.Exchange(ref _flushScheduled, 0);
      while (_pending.TryDequeue(out var logEvent)) LogEvents.Add(logEvent);
      while (LogEvents.Count > maxEvent) LogEvents.RemoveAt(0);
   }
}
