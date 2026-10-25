// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.ObjectModel;
using System.Windows;
using Entree.MVVM.Model;

namespace Entree.MVVM.View.Controls;

public partial class ThirdPartyLibrariesControl
{
   public static readonly DependencyProperty LibrariesProperty =
            DependencyProperty.Register(
                                        nameof(Libraries),
                                        typeof(ObservableCollection<LibraryInfo>),
                                        typeof(ThirdPartyLibrariesControl),
                                        new PropertyMetadata(null));

   public ThirdPartyLibrariesControl()
   {
      InitializeComponent();
      DataContext = this;
   }

   public ObservableCollection<LibraryInfo> Libraries
   {
      get => (ObservableCollection<LibraryInfo>)GetValue(LibrariesProperty);
      set => SetValue(LibrariesProperty, value);
   }
}