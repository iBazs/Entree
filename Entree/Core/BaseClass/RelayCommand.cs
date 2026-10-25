// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows.Input;

namespace Entree.Core.BaseClass;

public class RelayCommand(Action<object> execute, Predicate<object>? canExecute = null)
         : ICommand
{
   private readonly Action<object> _execute = execute ?? throw new ArgumentNullException(nameof(execute));

   public event EventHandler? CanExecuteChanged
   {
      add => CommandManager.RequerySuggested += value;
      remove => CommandManager.RequerySuggested -= value;
   }
#pragma warning disable CS8767
   public bool CanExecute(object parameter) => canExecute == null || canExecute(parameter);

   public void Execute(object parameter) => _execute(parameter);
#pragma warning restore CS8767
}