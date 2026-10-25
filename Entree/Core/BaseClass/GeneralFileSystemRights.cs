// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Security.AccessControl;

namespace Entree.Core.BaseClass;

[Flags]
public enum GenericRights
{
   GenericRead       = unchecked((int)0x80000000),
   GenericWrite      = 0x40000000,
   GenericExecute    = 0x20000000,
   GenericAll        = 0x10000000,
   StandardRightsAll = 0x001F0000,
   SpecificRightsAll = 0x0000FFFF
}

[Flags]
public enum MappedGenericRights
{
   FileGenericExecute = FileSystemRights.ExecuteFile | FileSystemRights.ReadPermissions |
                        FileSystemRights.ReadAttributes | FileSystemRights.Synchronize,

   FileGenericRead = FileSystemRights.ReadAttributes | FileSystemRights.ReadData |
                     FileSystemRights.ReadExtendedAttributes | FileSystemRights.ReadPermissions |
                     FileSystemRights.Synchronize,

   FileGenericWrite = FileSystemRights.AppendData | FileSystemRights.WriteAttributes | FileSystemRights.WriteData |
                      FileSystemRights.WriteExtendedAttributes | FileSystemRights.ReadPermissions |
                      FileSystemRights.Synchronize
}