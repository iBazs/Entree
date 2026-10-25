// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace Entree.MVVM.View.Converter;

public class PathToFolderNameConverter : IValueConverter
{
   public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            FolderName(value as string);

   public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Binding.DoNothing;

   public static string FolderName(string? path) =>
            string.IsNullOrEmpty(path) || Path.GetPathRoot(path) == path
                     ? path ?? string.Empty
                     : Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name
                              ? name
                              : path;
}
