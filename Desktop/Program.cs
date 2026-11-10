// CLT Desktop for macOS — the screen capture + input relay process.
// Launched by the agent when IT opens a remote control session. Connects to /remote/hubs/desktop,
// streams the screen via CoreGraphics, and relays mouse/keyboard input via CGEvent.
using AMS.RemoteMac.Shared;

namespace AMS.RemoteMac.Desktop;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        var session = ParseArgs(args);
        if (session is null)
        {
            Console.Error.WriteLine("Usage: CLT-Desktop --session <guid> --access-key <key> --user-connection <id> --requester <name> --org-name <name> --org-id <id>");
            return 1;
        }

        var info = ConnectionInfo.Load();
        if (info is null)
        {
            Console.Error.WriteLine("ConnectionInfo.json not found.");
            return 1;
        }

        // Check screen capture permission before connecting.
        if (!CoreGraphics.CGPreflightScreenCaptureAccess())
        {
            Console.Error.WriteLine("Screen Recording permission not granted. Open System Settings → Privacy & Security → Screen Recording and add CLT-Desktop.");
            CoreGraphics.CGRequestScreenCaptureAccess();
            return 2;
        }

        Console.WriteLine($"Desktop starting for session {session.SessionId}");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        await using var desktop = new DesktopConnection(info, session.SessionId, session.AccessKey,
            session.RequesterName, session.OrgName, session.OrgId);

        try
        {
            await desktop.ConnectAndRunAsync(cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Desktop error: {ex.Message}");
            return 1;
        }

        Console.WriteLine("Desktop stopped");
        return 0;
    }

    private sealed record SessionArgs(Guid SessionId, string AccessKey, string UserConnectionId,
        string RequesterName, string OrgName, string OrgId);

    private static SessionArgs? ParseArgs(string[] args)
    {
        string? sessionId = null, accessKey = null, userConnection = null, requester = null, orgName = null, orgId = null;

        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--session": sessionId = args[++i]; break;
                case "--access-key": accessKey = args[++i]; break;
                case "--user-connection": userConnection = args[++i]; break;
                case "--requester": requester = args[++i]; break;
                case "--org-name": orgName = args[++i]; break;
                case "--org-id": orgId = args[++i]; break;
            }
        }

        if (sessionId is null || accessKey is null || !Guid.TryParse(sessionId, out var sid))
            return null;

        return new SessionArgs(sid, accessKey, userConnection ?? "", requester ?? "", orgName ?? "", orgId ?? "");
    }
}
