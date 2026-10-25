// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using Entree.Core.BaseClass;
using System.ComponentModel;

namespace Entree.Services;

public interface INavigationService : INotifyPropertyChanged
{
   ViewModel CurrentView { get; }
   void      NavigateTo(Type viewModelType);
   void      NavigateTo<T>() where T : ViewModel => NavigateTo(typeof(T));
}

internal class NavigationService(Func<Type, ViewModel> viewModelFactory) : ObservableObject, INavigationService
{
   private ViewModel _currentView = null!;

   public ViewModel CurrentView
   {
      get => _currentView;
      private set => Set(ref _currentView, value);
   }

   public void NavigateTo(Type viewModelType) => CurrentView = viewModelFactory(viewModelType);
}