// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows;

namespace Entree.MVVM.View.Controls;

public partial class PageHeader
{
   public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeader),
                                        new PropertyMetadata(string.Empty));

   public static readonly DependencyProperty SubtitleProperty =
            DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(PageHeader),
                                        new PropertyMetadata(string.Empty));

   public PageHeader()
   {
      InitializeComponent();
   }

   public string Title
   {
      get => (string)GetValue(TitleProperty);
      set => SetValue(TitleProperty, value);
   }

   public string Subtitle
   {
      get => (string)GetValue(SubtitleProperty);
      set => SetValue(SubtitleProperty, value);
   }
}