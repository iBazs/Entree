// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Entree.Core.Helpers;

namespace Entree.Core.API;

internal static class SecurityDescriptorReader
{
   private const uint OwnerSecurityInformation  = 0x1;
   private const uint DaclSecurityInformation   = 0x4;
   private const int  ErrorFileNotFound         = 2;
   private const int  ErrorPathNotFound         = 3;
   private const int  ErrorAccessDenied         = 5;
   private const int  ErrorSharingViolation     = 32;
   private const int  ErrorFilenameExceedsRange = 206;
   private const int  ErrorInsufficientBuffer   = 122;
   private const int  InitialBufferSize         = 1024;

   [ThreadStatic] private static byte[]? _buffer;

   public static (byte[]? Descriptor, int Error) Read(string path, bool includeOwner)
   {
      var name   = PathHelper.ExtendedLengthPath(path);
      var info   = DaclSecurityInformation | (includeOwner ? OwnerSecurityInformation : 0);
      var buffer = _buffer ??= new byte[InitialBufferSize];
      while (true)
      {
         if (GetFileSecurityW(name, info, buffer, buffer.Length, out var needed))
            return (buffer[..(int)GetSecurityDescriptorLength(buffer)], 0);
         var error = Marshal.GetLastWin32Error();
         if (error != ErrorInsufficientBuffer || needed <= buffer.Length) return (null, error);
         buffer = _buffer = new byte[needed];
      }
   }

   public static Exception ToException(int error)
   {
      var message = new Win32Exception(error).Message;
      return error switch
             {
                      ErrorAccessDenied         => new UnauthorizedAccessException(message),
                      ErrorFileNotFound         => new FileNotFoundException(message),
                      ErrorPathNotFound         => new DirectoryNotFoundException(message),
                      ErrorFilenameExceedsRange => new PathTooLongException(message),
                      ErrorSharingViolation     => new IOException(message),
                      _                         => new Win32Exception(error)
             };
   }

   [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
   [return: MarshalAs(UnmanagedType.Bool)]
   private static extern bool GetFileSecurityW(string fileName, uint requestedInformation, byte[] securityDescriptor,
                                               int length, out int lengthNeeded);

   [DllImport("advapi32.dll", ExactSpelling = true)]
   private static extern uint GetSecurityDescriptorLength(byte[] securityDescriptor);
}
