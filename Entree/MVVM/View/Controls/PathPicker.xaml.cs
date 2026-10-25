// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace Entree.MVVM.View.Controls;

public partial class PathPicker
{
   public static readonly DependencyProperty PathProperty =
            DependencyProperty.Register(nameof(Path), typeof(string), typeof(PathPicker),
                                        new FrameworkPropertyMetadata(string.Empty,
                                                                      FrameworkPropertyMetadataOptions
                                                                               .BindsTwoWayByDefault));

   public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(PathPicker),
                                        new PropertyMetadata(string.Empty));

   public static readonly DependencyProperty BrowseCommandProperty =
            DependencyProperty.Register(nameof(BrowseCommand), typeof(ICommand), typeof(PathPicker),
                                        new PropertyMetadata(null));

   public static readonly DependencyProperty BrowseToolTipProperty =
            DependencyProperty.Register(nameof(BrowseToolTip), typeof(string), typeof(PathPicker),
                                        new PropertyMetadata("Browse"));

   public static readonly DependencyProperty BrowseIconProperty =
            DependencyProperty.Register(nameof(BrowseIcon), typeof(IconElement), typeof(PathPicker),
                                        new PropertyMetadata(null));

   public static readonly DependencyProperty IsReadOnlyProperty =
            DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(PathPicker),
                                        new PropertyMetadata(false));

   public PathPicker()
   {
      InitializeComponent();
      BrowseIcon ??= new SymbolIcon(SymbolRegular.FolderOpen24);
   }

   public string Path
   {
      get => (string)GetValue(PathProperty);
      set => SetValue(PathProperty, value);
   }

   public string Placeholder
   {
      get => (string)GetValue(PlaceholderProperty);
      set => SetValue(PlaceholderProperty, value);
   }

   public ICommand? BrowseCommand
   {
      get => (ICommand?)GetValue(BrowseCommandProperty);
      set => SetValue(BrowseCommandProperty, value);
   }

   public string BrowseToolTip
   {
      get => (string)GetValue(BrowseToolTipProperty);
      set => SetValue(BrowseToolTipProperty, value);
   }

   public IconElement? BrowseIcon
   {
      get => (IconElement?)GetValue(BrowseIconProperty);
      set => SetValue(BrowseIconProperty, value);
   }

   public bool IsReadOnly
   {
      get => (bool)GetValue(IsReadOnlyProperty);
      set => SetValue(IsReadOnlyProperty, value);
   }
}
