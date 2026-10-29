// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using Velopack;
using Entree.Services;

namespace Entree;

public static class Program
{
   [STAThread]
   public static int Main()
   {
      // Handles Velopack's install, update and uninstall hooks, which exit before the app starts.
      // Uninstall keeps the data folder and scheduled tasks, so a reinstall picks them up again.
      VelopackApp.Build()
                 .OnAfterInstallFastCallback(_ => DataMigration.Safely(DataMigration.FromSetupBackup))
                 .Run();
      DataMigration.Safely(DataMigration.FromPortable);

      var app = new App();
      app.InitializeComponent();
      return app.Run();
   }
}
