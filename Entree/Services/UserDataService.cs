// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using Serilog;
using Entree.Core.Helpers;

namespace Entree.Services;

public interface IUserDataService
{
   Task<bool> DeleteAllAsync();
}

// Uninstalling keeps the data folder and scheduled tasks; this removes both on request
public sealed class UserDataService(ITaskSchedulerService tasks, string dataDirectory) : IUserDataService
{
   public UserDataService(ITaskSchedulerService tasks) : this(tasks, PathHelper.DataDirectory) { }

   public async Task<bool> DeleteAllAsync()
   {
      if (!await tasks.DeleteAllJobsAsync())
      {
         Log.Error("Could not delete every scheduled task, kept the data folder {Directory}", dataDirectory);
         return false;
      }

      Log.Information("Deleted the scheduled tasks, deleting the data folder {Directory}", dataDirectory);
      // The log file is in the data folder and stays open until the logger closes
      await Log.CloseAndFlushAsync();
      try
      {
         if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
         return true;
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
         return false;
      }
   }
}
