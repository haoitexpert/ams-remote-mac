// Facts about this Mac that both the agent and the desktop process report.
using System.Diagnostics;

namespace AMS.RemoteMac.Shared;

public static class MacHost
{
    /// <summary>The name in System Settings → General → Sharing — what IT sets to the asset tag, and what the installer registers.</summary>
    public static string ComputerName()
    {
        var name = Run("/usr/sbin/scutil", "--get", "ComputerName").Trim();
        return name.Length > 0 ? name : Environment.MachineName;
    }

    /// <summary>The user signed in at the screen (owner of /dev/console), or null at the login window.</summary>
    public static (string Name, int Uid)? ConsoleUser()
    {
        var output = Run("/usr/bin/stat", "-f", "%Su %u", "/dev/console").Trim().Split(' ');
        if (output.Length != 2 || !int.TryParse(output[1], out var uid)) return null;
        if (uid == 0 || output[0] is "root" or "loginwindow" or "") return null;
        return (output[0], uid);
    }

    public static string Run(string fileName, params string[] args)
    {
        try
        {
            var start = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            foreach (var a in args) start.ArgumentList.Add(a);
            using var proc = Process.Start(start);
            if (proc is null) return string.Empty;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            return output;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return string.Empty;
        }
    }
}
