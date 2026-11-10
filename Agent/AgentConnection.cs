// SignalR client of AMS's AgentHub (/remote/hubs/service), speaking the same MessagePack protocol as the Windows CLT agent.
using System.Diagnostics;
using AMS.RemoteMac.Shared;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace AMS.RemoteMac.Agent;

internal sealed class AgentConnection : IAsyncDisposable
{
    private const string Uninstaller = "/usr/local/lib/CLT/uninstall.sh";

    private readonly ConnectionInfo _info;
    private HubConnection? _hub;

    public AgentConnection(ConnectionInfo info) => _info = info;

    public async Task ConnectAsync(CancellationToken ct)
    {
        if (_hub is not null) await _hub.DisposeAsync();

        _hub = new HubConnectionBuilder()
            .WithUrl(_info.HubUrl("service"))
            .AddMessagePackProtocol()
            .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
            .Build();
        RegisterServerCallbacks(_hub);

        // AgentHub knows which computer a connection is only after DeviceCameOnline; a reconnect is a new connection.
        _hub.Reconnected += async _ =>
        {
            try { await AnnounceAsync(CancellationToken.None); }
            catch (Exception ex) { Console.Error.WriteLine($"Re-announcing after reconnect failed: {ex.Message}"); }
        };

        await _hub.StartAsync(ct);
        await AnnounceAsync(ct);
    }

    /// <summary>Heartbeats until the connection is gone for good (automatic reconnects used up), then returns.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var hub = _hub!;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        if (hub.State == HubConnectionState.Disconnected) return;

        while (!ct.IsCancellationRequested)
        {
            var tick = Task.Delay(TimeSpan.FromMinutes(5), ct);
            if (await Task.WhenAny(tick, closed.Task) == closed.Task) return;
            await tick;
            if (hub.State != HubConnectionState.Connected) continue;
            try { await hub.InvokeAsync("DeviceHeartbeat", MacDeviceInfo.Collect(_info), ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { Console.Error.WriteLine($"Heartbeat failed: {ex.Message}"); }
        }
    }

    private async Task AnnounceAsync(CancellationToken ct)
    {
        var hub = _hub!;
        var accepted = await hub.InvokeAsync<bool>("DeviceCameOnline", MacDeviceInfo.Collect(_info), ct);
        if (!accepted) throw new InvalidOperationException("AMS refused this agent (wrong key or remote access is off).");
        await hub.InvokeAsync("CheckForPendingRemoteControlSessions", ct);
    }

    private void RegisterServerCallbacks(HubConnection hub)
    {
        hub.On<Guid, string, string, string, string, string>("RemoteControl",
            (sessionId, accessKey, _, requesterName, orgName, _) =>
            {
                Console.WriteLine($"Remote control requested by {requesterName} (session {sessionId})");
                DesktopLauncher.Launch(sessionId, accessKey, requesterName, orgName);
            });

        hub.On<string[], string, string, string, string, string, string>("RestartScreenCaster",
            (_, sessionId, accessKey, _, requesterName, orgName, _) =>
            {
                Console.WriteLine($"Restarting screen caster for session {sessionId}");
                if (Guid.TryParse(sessionId, out var sid)) DesktopLauncher.Launch(sid, accessKey, requesterName, orgName);
            });

        hub.On("TriggerHeartbeat", async () =>
        {
            try { await hub.InvokeAsync("DeviceHeartbeat", MacDeviceInfo.Collect(_info)); }
            catch (Exception ex) { Console.Error.WriteLine($"Heartbeat failed: {ex.Message}"); }
        });

        hub.On("InvokeCtrlAltDel", () => Console.WriteLine("Ctrl+Alt+Del requested: nothing to do on macOS"));

        hub.On("UninstallAgent", () =>
        {
            Console.WriteLine("Uninstall requested by AMS");
            // The script unloads this daemon as its very last step, which ends this process (and the script) with it.
            if (!File.Exists(Uninstaller))
            {
                Console.Error.WriteLine($"{Uninstaller} is missing: install the client again, then uninstall.");
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo("/bin/bash", new[] { Uninstaller }) { UseShellExecute = false });
            }
            catch (Exception ex) { Console.Error.WriteLine($"Could not start the uninstaller: {ex.Message}"); }
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
    }
}
