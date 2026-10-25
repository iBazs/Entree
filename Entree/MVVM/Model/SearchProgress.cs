// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

namespace Entree.MVVM.Model;

public sealed class SearchProgress
{
   private string? _currentFolder;
   private long    _folders;
   private long    _items;
   private string  _phase = "Queued";

   public string Phase
   {
      set => Volatile.Write(ref _phase, value);
   }

   public void EnterFolder(string path)
   {
      Interlocked.Increment(ref _folders);
      Volatile.Write(ref _currentFolder, path);
   }

   public void ItemFound() => Interlocked.Increment(ref _items);

   public Snapshot Read() => new(Volatile.Read(ref _phase), Interlocked.Read(ref _items), Interlocked.Read(ref _folders),
                                 Volatile.Read(ref _currentFolder) ?? string.Empty);

   public readonly record struct Snapshot(string Phase, long Items, long Folders, string CurrentFolder);
}
