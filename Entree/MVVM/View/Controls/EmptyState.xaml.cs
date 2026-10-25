// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows;
using Wpf.Ui.Controls;

namespace Entree.MVVM.View.Controls;

public partial class EmptyState
{
   public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(nameof(Icon), typeof(SymbolRegular), typeof(EmptyState),
                                        new PropertyMetadata(SymbolRegular.Info24));

   public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyState),
                                        new PropertyMetadata(string.Empty));

   public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register(nameof(Message), typeof(string), typeof(EmptyState),
                                        new PropertyMetadata(string.Empty));

   public static readonly DependencyProperty ActionProperty =
            DependencyProperty.Register(nameof(Action), typeof(object), typeof(EmptyState),
                                        new PropertyMetadata(null));

   public static readonly DependencyProperty IconSizeProperty =
            DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(EmptyState),
                                        new PropertyMetadata(40d));

   public static readonly DependencyProperty SpacingProperty =
            DependencyProperty.Register(nameof(Spacing), typeof(Thickness), typeof(EmptyState),
                                        new PropertyMetadata(new Thickness(0, 48, 0, 48)));

   public EmptyState()
   {
      InitializeComponent();
   }

   public SymbolRegular Icon
   {
      get => (SymbolRegular)GetValue(IconProperty);
      set => SetValue(IconProperty, value);
   }

   public string Title
   {
      get => (string)GetValue(TitleProperty);
      set => SetValue(TitleProperty, value);
   }

   public string Message
   {
      get => (string)GetValue(MessageProperty);
      set => SetValue(MessageProperty, value);
   }

   public object? Action
   {
      get => GetValue(ActionProperty);
      set => SetValue(ActionProperty, value);
   }

   public double IconSize
   {
      get => (double)GetValue(IconSizeProperty);
      set => SetValue(IconSizeProperty, value);
   }

   public Thickness Spacing
   {
      get => (Thickness)GetValue(SpacingProperty);
      set => SetValue(SpacingProperty, value);
   }
}
