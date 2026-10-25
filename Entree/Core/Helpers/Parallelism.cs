// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

namespace Entree.Core.Helpers;

internal static class Parallelism
{
   private const int MaxDegree = 512;

   public static int Degree(int threadLimit) =>
      threadLimit is > 0 and <= MaxDegree ? threadLimit : Math.Min(Environment.ProcessorCount, MaxDegree);
}
