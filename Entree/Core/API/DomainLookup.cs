// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Serilog;
using Entree.Core.BaseClass;
using Entree.MVVM.Model;

namespace Entree.Core.API;

internal static class DomainLookup
{
   private const uint DsDomainInForest        = 0x1;
   private const uint DsDomainDirectOutbound  = 0x2;
   private const int  ErrorInsufficientBuffer = 122;
   private const int  ErrorMoreData           = 234;
   private const int  NameBufferSize          = 256;

   public static Outcome<IReadOnlyList<TrustedDomain>> Trusts()
   {
      var error = DsEnumerateDomainTrustsW(null, DomainRouting.DsDomainPrimary | DsDomainInForest | DsDomainDirectOutbound,
                                           out var buffer, out var count);
      if (error != 0) return new Outcome<IReadOnlyList<TrustedDomain>>.Failed(new Win32Exception((int)error).Message);

      try
      {
         var size = Marshal.SizeOf<DsDomainTrusts>();
         return new Outcome<IReadOnlyList<TrustedDomain>>.Done(Enumerable.Range(0, (int)count)
                          .Select(i => Marshal.PtrToStructure<DsDomainTrusts>(buffer + i * size))
                          .SelectMany(t => DomainRouting.ToTrustedDomain(Marshal.PtrToStringUni(t.NetbiosDomainName),
                                                                         Marshal.PtrToStringUni(t.DnsDomainName), t.Flags,
                                                                         t.TrustType,
                                                                         t.DomainSid == IntPtr.Zero ? null : new SecurityIdentifier(t.DomainSid)))
                          .ToList());
      }
      finally
      {
         NetApiBufferFree(buffer);
      }
   }

   public static string? RemotePath(string? path)
   {
      if (path is not [var letter, ':', ..] || !char.IsAsciiLetter(letter)) return path;
      var length = NameBufferSize;
      while (true)
      {
         var remote = new char[length];
         var error  = WNetGetConnectionW(path[..2], remote, ref length);
         if (error == 0) return string.Concat(new string(remote).TrimEnd('\0'), path.AsSpan(2));
         if (error != ErrorMoreData || length <= remote.Length) return path;
      }
   }

   public static AccountInfo Lookup(SecurityIdentifier sid, string? server = null)
   {
      var binary = new byte[sid.BinaryLength];
      sid.GetBinaryForm(binary, 0);
      int nameLength = NameBufferSize, domainLength = NameBufferSize;
      while (true)
      {
         var name   = new char[nameLength];
         var domain = new char[domainLength];
         if (LookupAccountSidW(server, binary, name, ref nameLength, domain, ref domainLength, out var use))
         {
            var domainName = new string(domain, 0, domainLength);
            return new AccountInfo(sid, domainName.Length > 0 ? domainName : null, new string(name, 0, nameLength),
                                   DomainRouting.ToAccountType(sid, use), DomainRouting.StateWithoutDirectory(sid));
         }

         var error = Marshal.GetLastWin32Error();
         if (error == ErrorInsufficientBuffer && (nameLength > name.Length || domainLength > domain.Length)) continue;
         var message = new Win32Exception(error).Message;
         Log.Error("Failed to resolve SID {SecurityIdentifier} on {Server}: {Message}", sid, server ?? "this computer", message);
         return new AccountInfo(sid, error: message);
      }
   }

   [StructLayout(LayoutKind.Sequential)]
   private struct DsDomainTrusts
   {
      public IntPtr NetbiosDomainName;
      public IntPtr DnsDomainName;
      public uint   Flags;
      public uint   ParentIndex;
      public uint   TrustType;
      public uint   TrustAttributes;
      public IntPtr DomainSid;
      public Guid   DomainGuid;
   }

   [DllImport("netapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
   private static extern uint DsEnumerateDomainTrustsW(string? serverName, uint flags, out IntPtr domains,
                                                       out uint domainCount);

   [DllImport("netapi32.dll", ExactSpelling = true)]
   private static extern uint NetApiBufferFree(IntPtr buffer);

   [DllImport("mpr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
   private static extern int WNetGetConnectionW(string localName, char[] remoteName, ref int length);

   [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
   [return: MarshalAs(UnmanagedType.Bool)]
   private static extern bool LookupAccountSidW(string? systemName, byte[] sid, char[] name, ref int nameLength,
                                                char[] domainName, ref int domainLength, out int use);
}
