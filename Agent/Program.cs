// CLT Agent for macOS — the launchd daemon that keeps this computer connected to AMS.
// Equivalent of the Windows CLT service (Remotely_Service): connects via SignalR to /remote/hubs/service,
// reports DeviceClientDto every 5 minutes, and starts the desktop process when IT opens a session.
using AMS.RemoteMac.Shared;

namespace AMS.RemoteMac.Agent;

internal static class Program
{
    private static readonly CancellationTokenSource Cts = new();

    static async Task<int> Main(string[] args)
    {
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Cts.Cancel(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cts.Cancel();

        var info = ConnectionInfo.Load();
        if (info is null)
        {
            Console.Error.WriteLine("ConnectionInfo.json not found. Run the installer first.");
            return 1;
        }

        Console.WriteLine($"CLT Agent starting — server: {info.AmsUrl}, device: {info.DeviceId}");
        var agent = new AgentConnection(info);

        while (!Cts.IsCancellationRequested)
        {
            try
            {
                await agent.ConnectAsync(Cts.Token);
                Console.WriteLine("Connected to AMS");
                await agent.RunAsync(Cts.Token);
            }
            catch (OperationCanceledException) when (Cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Connection lost: {ex.Message}");
            }

            if (Cts.IsCancellationRequested) break;
            Console.WriteLine("Reconnecting in 10 seconds...");
            try { await Task.Delay(TimeSpan.FromSeconds(10), Cts.Token); }
            catch (OperationCanceledException) { break; }
        }

        await agent.DisposeAsync();
        Console.WriteLine("CLT Agent stopped");
        return 0;
    }
}
