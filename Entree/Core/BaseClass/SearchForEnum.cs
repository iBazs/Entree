// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.ComponentModel;

namespace Entree.Core.BaseClass;

public enum SearchFor
{
   [Description("Files")]             Files           = 0,
   [Description("Folders")]           Folders         = 1,
   [Description("Folders and Files")] FoldersAndFiles = 2
}