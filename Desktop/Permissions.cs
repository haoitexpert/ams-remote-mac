// Screen Recording and Accessibility, as macOS records them for this app (IT Remote Control). macOS lists an app on those
// pages of System Settings only after the app has asked once, so IT Remote Support's buttons run it with
// --request-permissions before opening the page, and with --check-permissions to show what is allowed.
using System.Text.Json;
using static AMS.RemoteMac.Desktop.CoreGraphics;

namespace AMS.RemoteMac.Desktop;

internal static class Permissions
{
    /// <summary>Read by IT Remote Support (PermissionHelper.swift); both run as the signed-in user.</summary>
    private static string StatusPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Application Support", "AMS Remote Support", "permissions.json");

    /// <summary>A process's own Screen Recording answer does not change while it runs, so only a fresh process reports it.</summary>
    public static void WriteStatus()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatusPath)!);
        File.WriteAllText(StatusPath, JsonSerializer.Serialize(new
        {
            screenRecording = CGPreflightScreenCaptureAccess(),
            accessibility = AXIsProcessTrusted(),
            checkedAt = DateTime.UtcNow,
        }));
    }

    /// <summary>Asks macOS for "screen", "accessibility" or "all", then waits: its prompt stays only while the asking process lives.</summary>
    public static async Task RequestAsync(string which)
    {
        var screen = which is "screen" or "all";
        var accessibility = which is "accessibility" or "all";
        if (screen && !CGPreflightScreenCaptureAccess()) CGRequestScreenCaptureAccess();
        if (accessibility && !AXIsProcessTrusted()) PromptForAccessibility();

        for (var i = 0; i < 90; i++)
        {
            if (!screen && AXIsProcessTrusted()) break;
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
}
