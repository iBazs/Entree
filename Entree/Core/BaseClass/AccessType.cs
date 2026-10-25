// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

namespace Entree.Core.BaseClass;

public enum AccessType : byte
{
   None        = 0,
   FullControl = 1,
   Modify      = 2,
   Write       = 3,
   Read        = 4,
   List        = 5,
   Special     = 6,
   Unknown     = 7,
   Deny        = 8
}