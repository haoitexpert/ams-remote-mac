// Launches the desktop capture process when AMS tells the agent to start a remote control session.
// The desktop process is a separate binary (CLT-Desktop) that connects to /remote/hubs/desktop.
using System.Diagnostics;
using AMS.RemoteMac.Shared;

namespace AMS.RemoteMac.Agent;

internal static class DesktopLauncher
{
    public static async Task LaunchAsync(ConnectionInfo info, Guid sessionId, string accessKey,
        string userConnectionId, string requesterName, string orgName, string orgId)
    {
        try
        {
            var desktopPath = FindDesktop();
            if (desktopPath is null)
            {
                Console.Error.WriteLine("CLT-Desktop binary not found");
                return;
            }

            var args = $"--session {sessionId} --access-key {accessKey} --user-connection {userConnectionId} " +
                       $"--requester \"{requesterName}\" --org-name \"{orgName}\" --org-id \"{orgId}\"";

            Console.WriteLine($"Launching desktop: {desktopPath} {args}");

            var startInfo = new ProcessStartInfo
            {
                FileName = desktopPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // The desktop process needs the GUI session (Screen Recording, Accessibility permissions).
            // On macOS, launchd daemons run as root outside the GUI session. For the desktop process to
            // capture the screen, it must run in the console user's session. Use launchctl to run it
            // under the logged-in user.
            var consoleUser = GetConsoleUserId();
            if (consoleUser is not null)
            {
                startInfo.FileName = "/bin/launchctl";
                startInfo.Arguments = $"asuser {consoleUser} {desktopPath} {args}";
            }

            Process.Start(startInfo);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to launch desktop: {ex.Message}");
        }
    }

    private static string? FindDesktop()
    {
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CLT-Desktop"),
            "/usr/local/lib/CLT/CLT-Desktop",
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? GetConsoleUserId()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/id",
                Arguments = $"-u {GetConsoleUser()}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(3000);
            return string.IsNullOrEmpty(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }

    private static string GetConsoleUser()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/sbin/scutil",
                Arguments = "--get ConsoleUser",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proc is null) return Environment.UserName;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(3000);
            return string.IsNullOrEmpty(output) || output == "loginwindow" ? Environment.UserName : output;
        }
        catch
        {
            return Environment.UserName;
        }
    }
}
