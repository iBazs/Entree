// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Entree.Core.BaseClass;

public class SafeFindHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
{
   protected override bool ReleaseHandle() => FindClose(handle);

   [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
   private static extern bool FindClose(IntPtr hFindFile);
}