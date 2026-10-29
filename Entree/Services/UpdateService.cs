// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace Entree.Services;

public interface IUpdateService
{
   bool          IsInstalled { get; }
   Task<string?> CheckAsync();
   Task          DownloadAndRestartAsync(Action<int> progress);
}

public sealed class UpdateService : IUpdateService
{
   public const string ReleasesUrl = "https://github.com/iBazs/Entree/releases";

   private readonly UpdateManager _manager = new(new GithubSource("https://github.com/iBazs/Entree", null,
                                                                  IsPrerelease()));
   private UpdateInfo? _available;

   // Portable exes are not installed by Velopack and update by downloading a new exe
   public bool IsInstalled => _manager.IsInstalled;

   public async Task<string?> CheckAsync()
   {
      if (!IsInstalled) return null;
      _available = await _manager.CheckForUpdatesAsync();
      return _available?.TargetFullRelease.Version.ToString();
   }

   public async Task DownloadAndRestartAsync(Action<int> progress)
   {
      if (_available is null) return;
      await _manager.DownloadUpdatesAsync(_available, progress);
      _manager.ApplyUpdatesAndRestart(_available);
   }

   private static bool IsPrerelease() =>
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                   ?.InformationalVersion.Split('+')[0].Contains('-') ?? false;
}
