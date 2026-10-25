// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace Entree.MVVM.View.Controls;

public partial class DashboardCard
{
   public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(DashboardCard),
                                        new PropertyMetadata(string.Empty));

   public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register(nameof(Description), typeof(string), typeof(DashboardCard),
                                        new PropertyMetadata(string.Empty));

   public static readonly DependencyProperty IconSymbolProperty =
            DependencyProperty.Register(nameof(IconSymbol), typeof(SymbolRegular), typeof(DashboardCard),
                                        new PropertyMetadata(SymbolRegular.Star24));

   public static readonly DependencyProperty AccentColorProperty =
            DependencyProperty.Register(nameof(AccentColor), typeof(Color), typeof(DashboardCard),
                                        new PropertyMetadata(Colors.Green));

   public static readonly DependencyProperty AccentColorSecondaryProperty =
            DependencyProperty.Register(nameof(AccentColorSecondary), typeof(Color), typeof(DashboardCard),
                                        new PropertyMetadata(Colors.DarkGreen));

   public static readonly DependencyProperty CardCommandProperty =
            DependencyProperty.Register(nameof(CardCommand), typeof(ICommand), typeof(DashboardCard),
                                        new PropertyMetadata(null));

   public DashboardCard()
   {
      InitializeComponent();
      if (!SystemParameters.ClientAreaAnimation) Triggers.Clear();
   }

   public string Title
   {
      get => (string)GetValue(TitleProperty);
      set => SetValue(TitleProperty, value);
   }

   public string Description
   {
      get => (string)GetValue(DescriptionProperty);
      set => SetValue(DescriptionProperty, value);
   }

   public SymbolRegular IconSymbol
   {
      get => (SymbolRegular)GetValue(IconSymbolProperty);
      set => SetValue(IconSymbolProperty, value);
   }

   public Color AccentColor
   {
      get => (Color)GetValue(AccentColorProperty);
      set => SetValue(AccentColorProperty, value);
   }

   public Color AccentColorSecondary
   {
      get => (Color)GetValue(AccentColorSecondaryProperty);
      set => SetValue(AccentColorSecondaryProperty, value);
   }

   public ICommand CardCommand
   {
      get => (ICommand)GetValue(CardCommandProperty);
      set => SetValue(CardCommandProperty, value);
   }
}