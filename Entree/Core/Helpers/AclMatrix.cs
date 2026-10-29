// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Entree.Core.BaseClass;
using Entree.MVVM.Model;

namespace Entree.Core.Helpers;

public static class AclMatrix
{
   public static IEnumerable<ReportHelper.MatrixRow> Create(IReadOnlyList<FileSystemStruct> entries,
                                                            IReadOnlyList<SecurityIdentifier> sids)
   {
      if (entries.Count == 0) yield break;

      var columns     = sids.Select((sid, index) => (sid, index)).ToDictionary(x => x.sid, x => x.index);
      var bySecurity  = new Dictionary<SecurityInfo, AccessType[]>(ReferenceEqualityComparer.Instance);
      var directories = new Dictionary<string, AccessType[]>(StringComparer.Ordinal);
      var rows        = new Dictionary<AccessType[], AccessType[]>(RowComparer.Instance);

      AccessType[] AccessOf(FileSystemStruct entry, bool isDirectory)
      {
         if (directories.TryGetValue(entry.Path, out var known)) return known;
         if (!bySecurity.TryGetValue(entry.Security, out var access))
            bySecurity[entry.Security] = access = Intern(rows, Row(entry, columns));
         if (isDirectory) directories.TryAdd(entry.Path, access);
         return access;
      }

      var first = entries[0];
      yield return new ReportHelper.MatrixRow(first.Path, AccessOf(first, true));

      for (var i = 1; i < entries.Count; i++)
      {
         var entry  = entries[i];
         var access = AccessOf(entry, entry.FileEntryAttributes.HasFlag(FileAttributes.Directory));
         if (!access.AsSpan().SequenceEqual(NearestAncestor(directories, entry.Path)))
            yield return new ReportHelper.MatrixRow(entry.Path, access);
      }
   }

   private static AccessType[] NearestAncestor(Dictionary<string, AccessType[]> directories, string path)
   {
      for (var folder = Path.GetDirectoryName(path); folder is not null; folder = Path.GetDirectoryName(folder))
         if (directories.TryGetValue(folder, out var access)) return access;
      return [];
   }

   private static AccessType[] Intern(Dictionary<AccessType[], AccessType[]> rows, AccessType[] row)
   {
      if (rows.TryGetValue(row, out var known)) return known;
      rows[row] = row;
      return row;
   }

   private sealed class RowComparer : IEqualityComparer<AccessType[]>
   {
      public static readonly RowComparer Instance = new();

      public bool Equals(AccessType[]? x, AccessType[]? y) => x.AsSpan().SequenceEqual(y);

      public int GetHashCode(AccessType[] row)
      {
         var hash = new HashCode();
         foreach (var access in row) hash.Add(access);
         return hash.ToHashCode();
      }
   }

   private static AccessType[] Row(FileSystemStruct entry, Dictionary<SecurityIdentifier, int> columns)
   {
      var row = new AccessType[columns.Count];
      foreach (var (sid, rights, type) in entry.Aces)
         if (columns.TryGetValue(sid, out var index))
            row[index] = type == AccessControlType.Deny ? AccessType.Deny : ReportHelper.GetAccessType(rights);
      return row;
   }
}
