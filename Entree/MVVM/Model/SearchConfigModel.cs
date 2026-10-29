// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Entree.Core.API;
using Entree.Core.BaseClass;

namespace Entree.MVVM.Model;

public class SearchConfigModel
{
   public required string SearchDirectory { get; set; }
   public required string SaveDirectory   { get; set; }

   public SearchFor SearchFor  { get; set; }
   public int       DepthLevel { get; set; }

   public bool LocalSearch     { get; set; }
   public bool InheritedAccess { get; set; }
   public bool ShowOwner       { get; set; }
   public bool ShowGroups      { get; set; }

   public bool     ShowMatrix  { get; set; }
   public bool     OpenResults { get; set; }
   public ExportAs ExportAs    { get; set; }

   [JsonIgnore] public ObservableCollection<DomainContext> Domains          { get; set; } = [];
                public ObservableCollection<string>        ExcludedAccounts { get; set; } = [];
}

public enum ExportAs
{
   Csv,
   Xlsx
}