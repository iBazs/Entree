// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows;

namespace Entree.MVVM.View.Controls;

public partial class ToggleSwitchWithLabel
{
   public static readonly DependencyProperty ToggleTextProperty =
            DependencyProperty.Register(nameof(ToggleText), typeof(string), typeof(ToggleSwitchWithLabel),
                                        new PropertyMetadata(string.Empty));

   public static readonly DependencyProperty ToggleTooltipProperty =
            DependencyProperty.Register(nameof(ToggleTooltip), typeof(string), typeof(ToggleSwitchWithLabel),
                                        new PropertyMetadata(string.Empty));

   public static readonly DependencyProperty IsCheckedProperty =
            DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(ToggleSwitchWithLabel),
                                        new FrameworkPropertyMetadata(false,
                                                                      FrameworkPropertyMetadataOptions
                                                                              .BindsTwoWayByDefault));


   public ToggleSwitchWithLabel()
   {
      InitializeComponent();
   }

   public string ToggleText
   {
      get => (string)GetValue(ToggleTextProperty);
      set => SetValue(ToggleTextProperty, value);
   }

   public string ToggleTooltip
   {
      get => (string)GetValue(ToggleTooltipProperty);
      set => SetValue(ToggleTooltipProperty, value);
   }

   public bool IsChecked
   {
      get => (bool)GetValue(IsCheckedProperty);
      set => SetValue(IsCheckedProperty, value);
   }
}