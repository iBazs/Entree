// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using Wpf.Ui.Controls;
using Entree.MVVM.Model;
using Entree.MVVM.ViewModel;
using Xunit;

namespace Entree.Tests;

public class JobViewModelTests
{
   [Theory]
   [InlineData("Running", SymbolRegular.Stop24)]
   [InlineData("Ready", SymbolRegular.Play24)]
   [InlineData("Disabled", SymbolRegular.Prohibited24)]
   [InlineData("Queued", SymbolRegular.Play24)]
   [InlineData("Unknown", SymbolRegular.Play24)]
   [InlineData("New", SymbolRegular.Play24)]
   public void ProgressIcon_MapsEveryState_WithoutErrorGlyph(string status, SymbolRegular expected)
   {
      var vm = new JobViewModel(new Job { Status = status });
      Assert.Equal(expected, vm.ProgressIcon);
      Assert.NotEqual(SymbolRegular.ErrorCircle24, vm.ProgressIcon);
   }

   [Theory]
   [InlineData("Running", ControlAppearance.Danger)]
   [InlineData("Ready", ControlAppearance.Success)]
   [InlineData("Disabled", ControlAppearance.Secondary)]
   public void ProgressAppearance_FollowsProgressIcon(string status, ControlAppearance expected)
   {
      var vm = new JobViewModel(new Job { Status = status });
      Assert.Equal(expected, vm.ProgressAppearance);
   }

   [Theory]
   [InlineData("Running")]
   [InlineData("Ready")]
   [InlineData("Disabled")]
   [InlineData("Unknown")]
   public void StatusColor_IsResolved_ForEveryState(string status)
   {
      var vm = new JobViewModel(new Job { Status = status });
      Assert.NotNull(vm.StatusColor);
   }
}
