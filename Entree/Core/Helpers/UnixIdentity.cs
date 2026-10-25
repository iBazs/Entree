// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Security.Principal;
using Entree.Core.BaseClass;
using Entree.MVVM.Model;

namespace Entree.Core.Helpers;

internal static class UnixIdentity
{
   public static bool TryDescribe(SecurityIdentifier sid, out AccountInfo account)
   {
      var found = TryClassify(sid.Value, out var label);
      account = new AccountInfo(sid, found ? label.Domain : null, found ? label.Name : null, label.Type);
      return found;
   }

   public static bool IsUnix(SecurityIdentifier sid) => TryClassify(sid.Value, out _);

   internal static bool TryClassify(string sid, out (string Domain, string Name, AccountType Type) label)
   {
      label = sid.Split('-') switch
              {
                       ["S", "1", "22", "1", var id] when IsNumber(id)      => ("Unix User", $"uid {id}", AccountType.User),
                       ["S", "1", "22", "2", var id] when IsNumber(id)      => ("Unix Group", $"gid {id}", AccountType.Group),
                       ["S", "1", "5", "88", "1", var id] when IsNumber(id) => ("NFS User", $"uid {id}", AccountType.User),
                       ["S", "1", "5", "88", "2", var id] when IsNumber(id) => ("NFS Group", $"gid {id}", AccountType.Group),
                       ["S", "1", "5", "88", "3", var mode] when IsNumber(mode) =>
                                ("NFS Mode", ModeText(uint.Parse(mode, CultureInfo.InvariantCulture)), AccountType.UnIdentified),
                       _ => ("", "", AccountType.UnIdentified)
              };
      return label.Domain.Length > 0;
   }

   internal static string ModeText(uint mode)
   {
      Span<char> text = stackalloc char[9];
      for (var i = 0; i < 9; i++) text[i] = (mode & (1u << (8 - i))) != 0 ? "rwx"[i % 3] : '-';
      if ((mode & 0x800) != 0) text[2] = text[2] == 'x' ? 's' : 'S';
      if ((mode & 0x400) != 0) text[5] = text[5] == 'x' ? 's' : 'S';
      if ((mode & 0x200) != 0) text[8] = text[8] == 'x' ? 't' : 'T';
      return $"{Convert.ToString(mode & 0xFFF, 8).PadLeft(4, '0')} {text}";
   }

   private static bool IsNumber(string value) =>
            value.Length > 0 && uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);
}
