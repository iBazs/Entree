// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Entree.Core.API;
using Xunit;
using Xunit.Abstractions;

namespace Entree.Tests;

public class SecurityDescriptorReaderTests(ITestOutputHelper output)
{
   [Fact]
   public void FileSecurity_MatchesNamedSecurityInfo()
   {
      var paths = Directory.EnumerateFileSystemEntries(Environment.SystemDirectory).Take(2000).ToList();
      Read(paths, Current);
      Read(paths, Named);
      var (current, currentTime) = Read(paths, Current);
      var (named, namedTime)     = Read(paths, Named);
      output.WriteLine($"{paths.Count} entries: GetFileSecurityW {currentTime.TotalMilliseconds:F0} ms, " +
                       $"GetNamedSecurityInfoW {namedTime.TotalMilliseconds:F0} ms");
      Assert.Equal(named, current);
   }

   private static (List<string> Results, TimeSpan Time) Read(List<string> paths, Func<string, string> read)
   {
      var stopwatch = Stopwatch.StartNew();
      var results   = paths.Select(read).ToList();
      return (results, stopwatch.Elapsed);
   }

   private static string Current(string path) => Describe(SecurityDescriptorReader.Read(path, true));

   private static string Named(string path)
   {
      var error = GetNamedSecurityInfoW(path, 1, 0x5, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var descriptor);
      if (error != 0) return Describe((null, (int)error));
      try
      {
         var bytes = new byte[GetSecurityDescriptorLength(descriptor)];
         Marshal.Copy(descriptor, bytes, 0, bytes.Length);
         return Describe((bytes, 0));
      }
      finally
      {
         LocalFree(descriptor);
      }
   }

   private static string Describe((byte[]? Descriptor, int Error) result) =>
            result.Descriptor is null
                     ? $"error {result.Error}"
                     : new RawSecurityDescriptor(result.Descriptor, 0)
                              .GetSddlForm(AccessControlSections.Owner | AccessControlSections.Access);

   [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
   private static extern uint GetNamedSecurityInfoW(string objectName, int objectType, uint securityInfo, IntPtr owner,
                                                    IntPtr group, IntPtr dacl, IntPtr sacl, out IntPtr descriptor);

   [DllImport("advapi32.dll", ExactSpelling = true)]
   private static extern uint GetSecurityDescriptorLength(IntPtr descriptor);

   [DllImport("kernel32.dll", ExactSpelling = true)]
   private static extern IntPtr LocalFree(IntPtr memory);
}
