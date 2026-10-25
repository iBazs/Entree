// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;

namespace Entree.Core.Handler;

internal static class SharedFile
{
   public static FileStream OpenRead(string filePath) =>
            new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
}
