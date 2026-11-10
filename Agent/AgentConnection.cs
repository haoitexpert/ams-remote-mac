// SignalR client matching AgentHub on the server (/remote/hubs/service).
// Speaks the same MessagePack protocol as the Windows CLT agent.
using AMS.RemoteMac.Shared;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace AMS.RemoteMac.Agent;

internal sealed class AgentConnection : IAsyncDisposable
{
    private readonly ConnectionInfo _info;
    private HubConnection? _hub;

    public AgentConnection(ConnectionInfo info) => _info = info;

    public async Task ConnectAsync(CancellationToken ct)
    {
        var hubUrl = _info.Host.TrimEnd('/');
        if (!hubUrl.EndsWith("/remote", StringComparison.OrdinalIgnoreCase))
            hubUrl += "/remote";
        hubUrl += "/hubs/service";

        _hub = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .AddMessagePackProtocol()
            .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
            .Build();

        RegisterServerCallbacks(_hub);
        await _hub.StartAsync(ct);

        var device = MacDeviceInfo.Collect(_info);
        var accepted = await _hub.InvokeAsync<bool>("DeviceCameOnline", device, ct);
        if (!accepted) throw new InvalidOperationException("AMS refused this agent (wrong key or remote access is off).");
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await _hub!.InvokeAsync("CheckForPendingRemoteControlSessions", ct);

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
            var device = MacDeviceInfo.Collect(_info);
            await _hub.InvokeAsync("DeviceHeartbeat", device, ct);
        }
    }

    private void RegisterServerCallbacks(HubConnection hub)
    {
        // Server → Agent: start the desktop process for a remote control session.
        hub.On<Guid, string, string, string, string, string>("RemoteControl",
            (sessionId, accessKey, userConnectionId, requesterName, orgName, orgId) =>
            {
                Console.WriteLine($"Remote control requested by {requesterName} (session {sessionId})");
                _ = DesktopLauncher.LaunchAsync(_info, sessionId, accessKey, userConnectionId, requesterName, orgName, orgId);
            });

        hub.On<string[], string, string, string, string, string, string>("RestartScreenCaster",
            (viewerIds, sessionId, accessKey, userConnectionId, requesterName, orgName, orgId) =>
            {
                Console.WriteLine($"Restarting screen caster for session {sessionId}");
                if (Guid.TryParse(sessionId, out var sid))
                    _ = DesktopLauncher.LaunchAsync(_info, sid, accessKey, userConnectionId, requesterName, orgName, orgId);
            });

        hub.On("TriggerHeartbeat", async () =>
        {
            var device = MacDeviceInfo.Collect(_info);
            await hub.InvokeAsync("DeviceHeartbeat", device);
        });

        hub.On("InvokeCtrlAltDel", () =>
        {
            // macOS has no Ctrl+Alt+Del; log it and ignore.
            Console.WriteLine("InvokeCtrlAltDel requested (not applicable on macOS)");
        });

        hub.On("UninstallAgent", () =>
        {
            Console.WriteLine("Uninstall requested");
            // Run the uninstaller and exit.
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                Environment.Exit(0);
            });
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
    }
}
