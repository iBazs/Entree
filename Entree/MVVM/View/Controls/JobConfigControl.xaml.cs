// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Serilog;
using Wpf.Ui.Controls;
using Entree.MVVM.ViewModel.Control;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace Entree.MVVM.View.Controls;

public partial class JobConfigControl
{
   public JobConfigControl()
   {
      InitializeComponent();
   }

   private async void ButtonBase_OnClick(object sender, RoutedEventArgs e)
   {
      e.Handled = true;
      try
      {
         if (sender is not FrameworkElement button) return;

         var dataItem = button.DataContext;

         var listView = FindAncestor<ItemsControl>(button);
         if (listView?.DataContext == null) return;

         var uiMessageBox = new MessageBox
                            {
                                     Title                   = "Delete Task",
                                     Content                 = "\nAre you sure you want to delete the selected task?\n",
                                     PrimaryButtonText       = "Delete",
                                     PrimaryButtonAppearance = ControlAppearance.Danger,
                                     CloseButtonText         = "Cancel",
                                     CloseButtonAppearance   = ControlAppearance.Secondary
                            };
         var dialogResult = await uiMessageBox.ShowDialogAsync();

         if (dialogResult == MessageBoxResult.Primary)
         {
            var viewModel = listView.DataContext as TaskViewModel;
            if (viewModel?.DeleteJobCommand != null && dataItem != null) viewModel.DeleteJobCommand.Execute(dataItem);
         }
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Error deleting task.");
      }
   }

   private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
   {
      do
      {
         if (current is T ancestor) return ancestor;

         if (current != null) current = VisualTreeHelper.GetParent(current);
      } while (current != null);

      return null;
   }
}