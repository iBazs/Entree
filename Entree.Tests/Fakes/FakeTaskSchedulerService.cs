// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using Entree.MVVM.ViewModel;
using Entree.Services;

namespace Entree.Tests.Fakes;

internal sealed class FakeTaskSchedulerService : ITaskSchedulerService
{
   public List<JobViewModel> Jobs { get; init; } = [];

   public Task<List<JobViewModel>> LoadJobsAsync() => Task.FromResult(Jobs);

   public Task<bool> SaveJobAsync(JobViewModel job, TaskScheduleSettings settings) => Task.FromResult(true);

   public Task<bool> DeleteJobAsync(string taskName) => Task.FromResult(true);

   public Task EnsureTaskFolderExistsAsync() => Task.CompletedTask;
}
