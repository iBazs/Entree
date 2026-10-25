// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

namespace Entree.Core.Helpers;

public static class CommandLineHelper
{
   private static readonly string[] CommandStyle = ["-", "--", "/"];
   public static           bool     IsCommand(string input) => CommandStyle.Any(input.StartsWith);

   public static string TrimCommand(string input) =>
            IsCommand(input) ? input.Contains(CommandStyle[1]) ? input[2..] : input[1..] : input;
}