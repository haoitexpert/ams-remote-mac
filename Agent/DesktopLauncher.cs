// Starts IT Remote Control (CLT-Desktop in AMS Remote Control.app) when AMS opens a remote control session.
// The agent is a root daemon outside any GUI session. The app must run as the signed-in user, in that user's session, and be
// launched by LaunchServices (`open`), not as the agent's child: macOS gives Screen Recording and Accessibility to the
// process responsible for the request, and only an app LaunchServices started is responsible for itself — so the permission
// the user turned on for "IT Remote Control" in System Settings applies. `launchctl asuser` enters the user's session,
// `sudo -u` takes the user's identity.
using System.Diagnostics;
using AMS.RemoteMac.Shared;

namespace AMS.RemoteMac.Agent;

internal static class DesktopLauncher
{
    public const string AppPath = "/usr/local/lib/CLT/AMS Remote Control.app";

    public static void Launch(Guid sessionId, string accessKey, string requesterName, string orgName)
    {
        try
        {
            if (!Directory.Exists(AppPath))
            {
                Console.Error.WriteLine($"{AppPath} is not installed: run the installer again.");
                return;
            }
            if (MacHost.ConsoleUser() is not { } user)
            {
                Console.Error.WriteLine("Nobody is signed in at this Mac (login window): there is no screen session to share.");
                return;
            }

            var start = new ProcessStartInfo("/bin/launchctl") { UseShellExecute = false };
            foreach (var arg in new[]
                     {
                         "asuser", user.Uid.ToString(), "/usr/bin/sudo", "-u", user.Name, "--",
                         "/usr/bin/open", "-n", "-g", AppPath, "--args",
                         "--session", sessionId.ToString(), "--access-key", accessKey,
                         "--requester", requesterName, "--org-name", orgName,
                     })
                start.ArgumentList.Add(arg);

            Console.WriteLine($"Launching IT Remote Control as {user.Name} for session {sessionId} (its log: ~/Library/Logs/IT Remote Control.log)");
            using var _ = Process.Start(start);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to launch IT Remote Control: {ex.Message}");
        }
    }
}
