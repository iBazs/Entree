// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

namespace Entree.MVVM.Model;

public class LibraryInfo
{
   public string Name        { get; set; } = string.Empty;
   public string Creator     { get; set; } = string.Empty;
   public string Link        { get; set; } = string.Empty;
   public string LicenseType { get; set; } = string.Empty;
   public string LicenseText { get; set; } = string.Empty;
}