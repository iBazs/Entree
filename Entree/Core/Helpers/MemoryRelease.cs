// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Runtime;

namespace Entree.Core.Helpers;

public static class MemoryRelease
{
   public static Task AfterLargeOperation() =>
            Task.Run(() =>
            {
               GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
               GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
            });
}
