// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Entree.Core.Helpers;

public static class AppTheme
{
   public const string System = "System";
   public const string Light  = "Light";
   public const string Dark   = "Dark";

   private static readonly Color Accent = Color.FromRgb(56, 176, 0);
   private static          string? _choice;

   static AppTheme() =>
            ApplicationThemeManager.Changed += (theme, _) => ApplicationAccentColorManager.Apply(Accent, theme, true);

   public static IReadOnlyList<string> Choices { get; } = [System, Light, Dark];

   public static void Apply(string? choice, Window? window)
   {
      _choice = choice;
      var theme = Resolve(choice, ApplicationThemeManager.GetSystemTheme());
      ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, false);
      ApplicationAccentColorManager.Apply(Accent, theme, true);
      if (window is null) return;
      if (!window.IsLoaded)
      {
         window.Loaded -= OnWindowLoaded;
         window.Loaded += OnWindowLoaded;
         return;
      }

      SystemThemeWatcher.UnWatch(window);
      if (choice is not (Light or Dark)) SystemThemeWatcher.Watch(window, WindowBackdropType.Mica, false);
   }

   private static void OnWindowLoaded(object sender, RoutedEventArgs e)
   {
      var window = (Window)sender;
      window.Loaded -= OnWindowLoaded;
      Apply(_choice, window);
   }

   internal static ApplicationTheme Resolve(string? choice, SystemTheme system) =>
            choice switch
            {
                     Light => ApplicationTheme.Light,
                     Dark  => ApplicationTheme.Dark,
                     _ => system is SystemTheme.Light or SystemTheme.HCWhite or SystemTheme.Sunrise or SystemTheme.Flow
                                   ? ApplicationTheme.Light
                                   : ApplicationTheme.Dark
            };
}
