// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace Entree.MVVM.View.Controls;
public partial class StatusBadge
{
   public static readonly DependencyProperty StatusBackgroundProperty =
            DependencyProperty.Register(
                                        nameof(StatusBackground),
                                        typeof(Brush),
                                        typeof(StatusBadge),
                                        new PropertyMetadata(Brushes.Gray));
   public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(
                                        nameof(Icon),
                                        typeof(SymbolRegular),
                                        typeof(StatusBadge),
                                        new PropertyMetadata(SymbolRegular.Empty));
   public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(
                                        nameof(Text),
                                        typeof(string),
                                        typeof(StatusBadge),
                                        new PropertyMetadata(string.Empty));

   public StatusBadge()
   {
      InitializeComponent();
   }
   public Brush StatusBackground
   {
      get => (Brush)GetValue(StatusBackgroundProperty);
      set => SetValue(StatusBackgroundProperty, value);
   }
   public SymbolRegular Icon
   {
      get => (SymbolRegular)GetValue(IconProperty);
      set => SetValue(IconProperty, value);
   }
   public string Text
   {
      get => (string)GetValue(TextProperty);
      set => SetValue(TextProperty, value);
   }
}