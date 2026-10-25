// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using System.Runtime.InteropServices;

namespace Entree.MVVM.Model;

[Serializable]
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
[BestFitMapping(false)]
public struct Win32FindData
{
   public FileAttributes dwFileAttributes;
   public uint           ftCreationTime_dwLowDateTime;
   public uint           ftCreationTime_dwHighDateTime;
   public uint           ftLastAccessTime_dwLowDateTime;
   public uint           ftLastAccessTime_dwHighDateTime;
   public uint           ftLastWriteTime_dwLowDateTime;
   public uint           ftLastWriteTime_dwHighDateTime;
   public uint           nFileSizeHigh;
   public uint           nFileSizeLow;
   public int            dwReserved0;
   public int            dwReserved1;

   [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
   public string cFileName;

   [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
   public string cAlternateFileName;
}