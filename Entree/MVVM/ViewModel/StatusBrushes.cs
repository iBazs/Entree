// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows.Media;

namespace Entree.MVVM.ViewModel;

public static class StatusBrushes
{
   public static readonly Brush Success = Frozen(0x15, 0x80, 0x3D);
   public static readonly Brush Info    = Frozen(0x1D, 0x4E, 0xD8);
   public static readonly Brush Neutral = Frozen(0x4B, 0x55, 0x63);
   public static readonly Brush Danger  = Frozen(0xB9, 0x1C, 0x1C);
   public static readonly Brush Caution = Frozen(0xB4, 0x53, 0x09);
   public static readonly Brush Accent  = Frozen(0x6D, 0x28, 0xD9);

   public static Brush Frozen(byte r, byte g, byte b)
   {
      var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
      brush.Freeze();
      return brush;
   }
}
