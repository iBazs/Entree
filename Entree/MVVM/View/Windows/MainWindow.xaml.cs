// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows;
using Wpf.Ui.Animations;

namespace Entree.MVVM.View.Windows;

public partial class MainWindow
{
   private const int PageTransitionMilliseconds = 200;

   public MainWindow()
   {
      InitializeComponent();
      PageHost.Navigated += (_, _) =>
      {
         if (SystemParameters.ClientAreaAnimation)
            TransitionAnimationProvider.ApplyTransition(PageHost, Transition.FadeInWithSlide, PageTransitionMilliseconds);
      };
   }
}
