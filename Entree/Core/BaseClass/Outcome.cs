// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

namespace Entree.Core.BaseClass;

internal abstract record Outcome<T>
{
   private Outcome() { }

   public sealed record Done(T Value) : Outcome<T>;

   public sealed record Failed(string Reason) : Outcome<T>;
}
