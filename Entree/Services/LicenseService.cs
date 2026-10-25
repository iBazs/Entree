// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Serilog;
using Wpf.Ui.Controls;
using Entree.MVVM.Model;
using Entree.MVVM.View.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace Entree.Services;

public class LicenseService
{
   private const string LicenseText = """
                                      GNU GENERAL PUBLIC LICENSE
                                      Version 3, 29 June 2007

                                      Copyright (C) 2025 Bazs

                                      This program is free software: you can redistribute it and/or modify
                                      it under the terms of the GNU General Public License as published by
                                      the Free Software Foundation, either version 3 of the License, or
                                      (at your option) any later version.

                                      This program is distributed in the hope that it will be useful,
                                      but WITHOUT ANY WARRANTY; without even the implied warranty of
                                      MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
                                      GNU General Public License for more details.

                                      You should have received a copy of the GNU General Public License
                                      along with this program. If not, see <https://www.gnu.org/licenses/>.

                                      Full license text available at: https://www.gnu.org/licenses/gpl-3.0.html
                                      """;

   public async Task ShowLicenseAsync()
   {
      try
      {
         var scrollViewer = new ScrollViewer
                            {
                                     VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                                     Margin                      = new Thickness(0, 12, 0, 0),
                                     MaxHeight                   = 500
                            };

         var textBlock = new TextBlock
                         {
                                  FontTypography = FontTypography.Body,
                                  Text           = LicenseText,
                                  TextWrapping   = TextWrapping.Wrap,
                                  FontFamily     = new FontFamily("Consolas"),
                                  FontSize       = 12
                         };

         scrollViewer.Content = textBlock;
         var uiMessageBox = new MessageBox
                            {
                                     Title   = "Entrée License",
                                     Content = scrollViewer,
                            };
         await uiMessageBox.ShowDialogAsync();
      }
      catch (Exception ex)
      {
         await new MessageBox
               {
                        Title   = "GPL-3.0 License",
                        Content = $"{LicenseText}\n\nError: {ex.Message}"
               }.ShowDialogAsync();
      }
   }

   private static List<LibraryInfo> LoadLibraryInformation()
   {
      var libraries = new List<LibraryInfo>();
      var assembly  = typeof(LicenseService).Assembly;

      // Embedded as Licenses/<Library>\info.txt and Licenses/<Library>\LICENSE.txt
      foreach (var infoFile in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Licenses/")
                                                                           && n.EndsWith("info.txt")))
      {
         var licenseFile = infoFile[..^"info.txt".Length] + "LICENSE.txt";
         try
         {
            var infoText    = ReadResource(infoFile);
            var licenseText = ReadResource(licenseFile);
            if (infoText is null || licenseText is null)
               continue;

            var library   = new LibraryInfo();
            var infoLines = infoText.Split(["\r\n", "\n"], StringSplitOptions.None);

            foreach (var line in infoLines)
            {
               var parts = line.Split([':'], 2);
               if (parts.Length != 2) continue;

               var key   = parts[0].Trim();
               var value = parts[1].Trim();

               switch (key)
               {
                  case "Name":
                     library.Name = value;
                     break;
                  case "Creator":
                     library.Creator = value;
                     break;
                  case "Link":
                     library.Link = value;
                     break;
                  case "License":
                     library.LicenseType = value;
                     break;
               }
            }

            library.LicenseText = licenseText;
            libraries.Add(library);
         }
         catch (Exception ex)
         {
            Log.Warning(ex, "Could not read license {File}", licenseFile);
         }
      }

      return libraries.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();

      string? ReadResource(string name)
      {
         using var stream = assembly.GetManifestResourceStream(name);
         if (stream is null)
            return null;
         using var reader = new StreamReader(stream);
         return reader.ReadToEnd();
      }
   }

   public async Task ShowThirdPartyLicenseAsync()
   {
      try
      {
         var libraries = LoadLibraryInformation();
         if (libraries.Count == 0)
         {
            System.Windows.MessageBox.Show("No third-party library information found.", "Information",
                                           MessageBoxButton.OK, MessageBoxImage.Information);
            return;
         }

         var librariesControl = new ThirdPartyLibrariesControl
                                {
                                         Libraries = new ObservableCollection<LibraryInfo>(libraries)
                                };

         var mainWindowWidth = Application.Current.MainWindow?.Width ?? 960;
         var uiMessageBox = new MessageBox
                            {
                                     Width   = mainWindowWidth / 2,
                                     Title   = "Used Libraries",
                                     Content = librariesControl
                            };
         await uiMessageBox.ShowDialogAsync();
      }
      catch (Exception ex)
      {
         System.Windows.MessageBox.Show($"Error displaying used libraries: {ex.Message}", "Error",
                                        MessageBoxButton.OK, MessageBoxImage.Error);
      }
   }
}