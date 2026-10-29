// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using Serilog;

namespace Entree.Services;

public sealed class InstanceChannel : IDisposable
{
   private const int ConnectTimeoutMs = 3000;
   private const int RetryDelayMs     = 500;

   private static readonly string Name =
            $"Entree-{WindowsIdentity.GetCurrent().User?.Value}-{Process.GetCurrentProcess().SessionId}{(Environment.IsPrivilegedProcess ? "-admin" : "")}";

   private readonly CancellationTokenSource _listening = new();
   private readonly Mutex                   _mutex;

   public InstanceChannel()
   {
      _mutex    = new Mutex(true, $@"Local\{Name}", out var created);
      IsPrimary = created;
   }

   public bool IsPrimary { get; }

   public void Dispose()
   {
      _listening.Cancel();
      if (IsPrimary) _mutex.ReleaseMutex();
      _mutex.Dispose();
   }

   public async Task<int?> TryForwardAsync(string[] args)
   {
      await using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.InOut,
                                                       PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
      try
      {
         await pipe.ConnectAsync(ConnectTimeoutMs);
      }
      catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
      {
         Log.Warning("The running Entrée did not answer: {Message}", ex.Message);
         return null;
      }

      try
      {
         using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
         using var reader = new StreamReader(pipe, leaveOpen: true);
         await writer.WriteLineAsync(JsonSerializer.Serialize(args));
         Log.Information("Handed {Args} to the running Entrée", string.Join(" ", args));
         return int.TryParse(await reader.ReadLineAsync(), out var exitCode) ? exitCode : null;
      }
      catch (IOException ex)
      {
         Log.Warning("The running Entrée stopped before answering, running here instead: {Message}", ex.Message);
         return null;
      }
   }

   public void Listen(Func<string[], Task<int>> handle) => _ = AcceptAsync(handle, _listening.Token);

   private static async Task AcceptAsync(Func<string[], Task<int>> handle, CancellationToken token)
   {
      while (!token.IsCancellationRequested)
      {
         NamedPipeServerStream? server = null;
         try
         {
            server = new NamedPipeServerStream(Name, PipeDirection.InOut,
                                               NamedPipeServerStream.MaxAllowedServerInstances,
                                               PipeTransmissionMode.Byte,
                                               PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await server.WaitForConnectionAsync(token);
            _ = ServeAsync(server, handle);
         }
         catch (OperationCanceledException)
         {
            if (server is not null) await server.DisposeAsync();
         }
         catch (Exception ex)
         {
            if (server is not null) await server.DisposeAsync();
            Log.Warning(ex, "Could not accept a request from another Entrée");
            await Task.Delay(RetryDelayMs, CancellationToken.None);
         }
      }
   }

   private static async Task ServeAsync(NamedPipeServerStream server, Func<string[], Task<int>> handle)
   {
      await using (server)
      {
         try
         {
            using var reader = new StreamReader(server, leaveOpen: true);
            using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
            var       args   = JsonSerializer.Deserialize<string[]>(await reader.ReadLineAsync() ?? "[]") ?? [];
            Log.Information("Received {Args} from another Entrée", string.Join(" ", args));
            await writer.WriteLineAsync((await handle(args)).ToString());
         }
         catch (Exception ex)
         {
            Log.Warning(ex, "Could not serve a request from another Entrée");
         }
      }
   }
}
