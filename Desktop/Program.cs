// CLT Desktop for macOS — the screen capture + input relay process, packaged as the app "IT Remote Control"
// (/usr/local/lib/CLT/AMS Remote Control.app) so that macOS grants Screen Recording and Accessibility to it by name.
// Started through LaunchServices, as the signed-in user: by the agent for a session (connects to /remote/hubs/desktop,
// streams the screen via CoreGraphics, relays input via CGEvent), or by IT Remote Support with --check-permissions /
// --request-permissions.
using AMS.RemoteMac.Shared;

namespace AMS.RemoteMac.Desktop;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        LogToFile();

        switch (args.FirstOrDefault())
        {
            case "--check-permissions":
                Permissions.WriteStatus();
                return 0;
            case "--request-permissions":
                await Permissions.RequestAsync(args.ElementAtOrDefault(1) ?? "all");
                return 0;
        }

        var session = ParseArgs(args);
        if (session is null)
        {
            Console.Error.WriteLine("Usage: CLT-Desktop --session <guid> --access-key <key> [--requester <name>] [--org-name <name>]");
            return 1;
        }

        var info = ConnectionInfo.Load();
        if (info is null)
        {
            Console.Error.WriteLine("ConnectionInfo.json not found.");
            return 1;
        }

        // Asks macOS once to show the user the Screen Recording prompt; the session goes on either way and the viewer is told
        // what is missing (DesktopConnection), so IT is not left looking at a spinner.
        if (!CoreGraphics.CGPreflightScreenCaptureAccess()) CoreGraphics.CGRequestScreenCaptureAccess();

        Console.WriteLine($"Desktop starting for session {session.SessionId}");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

        await using var desktop = new DesktopConnection(info, session.SessionId, session.AccessKey, session.RequesterName, session.OrgName);
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

    /// <summary>An app started by LaunchServices has no terminal: its output goes to ~/Library/Logs/IT Remote Control.log.</summary>
    private static void LogToFile()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "IT Remote Control.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var mode = File.Exists(path) && new FileInfo(path).Length > 5_000_000 ? FileMode.Create : FileMode.Append;
            var log = new StreamWriter(new FileStream(path, mode, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
            Console.SetOut(log);
            Console.SetError(log);
            Console.WriteLine($"── {DateTime.Now:yyyy-MM-dd HH:mm:ss} {string.Join(' ', Environment.GetCommandLineArgs().Skip(1).Take(2))}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private sealed record SessionArgs(Guid SessionId, string AccessKey, string RequesterName, string OrgName);

    private static SessionArgs? ParseArgs(string[] args)
    {
        string? sessionId = null, accessKey = null, requester = null, orgName = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--session": sessionId = args[++i]; break;
                case "--access-key": accessKey = args[++i]; break;
                case "--requester": requester = args[++i]; break;
                case "--org-name": orgName = args[++i]; break;
            }
        }

        if (sessionId is null || accessKey is null || !Guid.TryParse(sessionId, out var sid)) return null;
        return new SessionArgs(sid, accessKey, requester ?? "", orgName ?? "");
    }
}
