// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using System.Windows;
using System.Windows.Media.Animation;
using Serilog;
using Entree.MVVM.ViewModel.Control;

namespace Entree.MVVM.View.Pages;

public partial class CreateConfigView
{
   public CreateConfigView()
   {
      InitializeComponent();
   }

   private void UIElement_OnDrop(object sender, DragEventArgs e)
   {
      ShowDropPanel(false);
      try
      {
         if (sender is not FrameworkElement element) return;
         if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
         if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
         var dataContext = element.DataContext as CreateConfigViewModel;
         foreach (var file in files)
         {
            if (!IsConfigFile(file))
            {
               Log.Error("File is not correct config file: {File}", file);
               e.Handled = true;
               e.Effects = DragDropEffects.None;
               return;
            }

            e.Effects = DragDropEffects.Copy;
            dataContext?.DragDropLoadConfigFileCommand.Execute(file);
         }
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Error loading config file.");
      }
   }

   private void UIElement_OnDragEnter(object sender, DragEventArgs e)
   {
      try
      {
         if (sender is not FrameworkElement) return;
         if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
         var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
         if (files == null) return;
         if (files.Any(file => !IsConfigFile(file)))
         {
            e.Handled = true;
            e.Effects = DragDropEffects.None;
            return;
         }

         ShowDropPanel(true);
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Error checking config file.");
      }
   }

   private static bool IsConfigFile(string file) =>
      Path.GetExtension(file).Equals(".entree", StringComparison.OrdinalIgnoreCase);

   private void UIElement_OnDragLeave(object sender, DragEventArgs e) => ShowDropPanel(false);

   private void ShowDropPanel(bool show)
   {
      DragDropPanel.IsHitTestVisible = show;
      DragDropPanel.BeginAnimation(OpacityProperty,
                                   new DoubleAnimation(show ? 1 : 0,
                                                       TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 150 : 0)));
   }
}