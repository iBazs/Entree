// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.ObjectModel;
using Serilog.Events;

namespace Entree.Services;

public class LogViewerService(MemorySink memorySink)
{
   public ObservableCollection<LogEvent> LogEvents => memorySink.LogEvents;
}