// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Entree.Core.BaseClass;

public class ObservableObject : INotifyPropertyChanged
{
   public event PropertyChangedEventHandler? PropertyChanged;

   protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
   {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
   }

   protected void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
   {
      field = value;
      OnPropertyChanged(propertyName);
   }

   protected void SetIfChanged<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
   {
      if (EqualityComparer<T>.Default.Equals(field, value)) return;
      Set(ref field, value, propertyName);
   }
}