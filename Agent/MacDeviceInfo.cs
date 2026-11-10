// Collects macOS system info for the DeviceClientDto the agent sends to AMS.
// Uses sysctl, sw_vers and IOKit via command-line tools (no P/Invoke needed here).
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using AMS.RemoteMac.Shared;

namespace AMS.RemoteMac.Agent;

internal static class MacDeviceInfo
{
    private const string AgentVersion = "1.0.0-mac";

    public static DeviceClientDto Collect(ConnectionInfo info)
    {
        var dto = new DeviceClientDto
        {
            AgentVersion = AgentVersion,
            ID = info.DeviceId,
            OrganizationID = info.OrganizationId,
            DeviceName = MacHost.ComputerName(),
            Platform = "macOS",
            Is64Bit = true,
            IsOnline = true,
            OSArchitecture = RuntimeInformation.OSArchitecture,
            OSDescription = GetOsDescription(),
            ProcessorCount = Environment.ProcessorCount,
            CurrentUser = MacHost.ConsoleUser()?.Name ?? string.Empty,
        };

        CollectMemory(dto);
        CollectStorage(dto);
        CollectMacAddresses(dto);
        CollectCpu(dto);

        return dto;
    }

    private static string GetOsDescription()
    {
        var name = Run("sw_vers", "-productName").Trim();
        var version = Run("sw_vers", "-productVersion").Trim();
        var build = Run("sw_vers", "-buildVersion").Trim();
        if (string.IsNullOrEmpty(name)) return RuntimeInformation.OSDescription;
        return $"{name} {version} ({build})";
    }

    private static void CollectMemory(DeviceClientDto dto)
    {
        // sysctl hw.memsize returns total physical RAM in bytes.
        if (ulong.TryParse(Run("sysctl", "-n hw.memsize").Trim(), out var bytes))
            dto.TotalMemory = bytes / (1024.0 * 1024.0 * 1024.0);

        // vm_stat gives page-level memory stats; Used = total - free - speculative.
        var vmstat = Run("vm_stat", "");
        var pageSize = 16384UL; // default on Apple Silicon; Intel is 4096
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64) pageSize = 4096;
        var free = ParseVmStat(vmstat, "Pages free");
        var spec = ParseVmStat(vmstat, "Pages speculative");
        if (free > 0)
        {
            var freeBytes = (free + spec) * pageSize;
            dto.UsedMemory = dto.TotalMemory - freeBytes / (1024.0 * 1024.0 * 1024.0);
            if (dto.UsedMemory < 0) dto.UsedMemory = 0;
        }
    }

    private static ulong ParseVmStat(string output, string label)
    {
        foreach (var line in output.Split('\n'))
        {
            if (!line.Contains(label, StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split(':');
            if (parts.Length < 2) continue;
            if (ulong.TryParse(parts[1].Trim().TrimEnd('.'), out var pages)) return pages;
        }
        return 0;
    }

    private static void CollectStorage(DeviceClientDto dto)
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .Select(d => new Drive
                {
                    DriveType = d.DriveType,
                    RootDirectory = d.RootDirectory.FullName,
                    Name = d.Name,
                    DriveFormat = d.DriveFormat,
                    FreeSpace = d.AvailableFreeSpace,
                    TotalSize = d.TotalSize,
                    VolumeLabel = d.VolumeLabel,
                })
                .ToList();

            dto.Drives = drives;
            dto.TotalStorage = drives.Sum(d => d.TotalSize) / (1024.0 * 1024.0 * 1024.0);
            dto.UsedStorage = drives.Sum(d => d.TotalSize - d.FreeSpace) / (1024.0 * 1024.0 * 1024.0);
        }
        catch
        {
            // Ignore storage errors.
        }
    }

    private static void CollectMacAddresses(DeviceClientDto dto)
    {
        try
        {
            dto.MacAddresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(n => n.GetPhysicalAddress().ToString())
                .Where(m => !string.IsNullOrEmpty(m) && m != "000000000000")
                .Distinct()
                .ToArray();
        }
        catch
        {
            dto.MacAddresses = Array.Empty<string>();
        }
    }

    private static void CollectCpu(DeviceClientDto dto)
    {
        // top -l 1 gives a snapshot; parse "CPU usage: X% user, Y% sys, Z% idle"
        var top = Run("top", "-l 1 -n 0 -s 0");
        foreach (var line in top.Split('\n'))
        {
            if (!line.StartsWith("CPU usage:", StringComparison.OrdinalIgnoreCase)) continue;
            // "CPU usage: 5.26% user, 3.94% sys, 90.79% idle"
            var idle = 0.0;
            var parts = line.Split(',');
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Contains("idle", StringComparison.OrdinalIgnoreCase))
                {
                    var pct = trimmed.Split('%')[0].Trim();
                    if (pct.Contains(' ')) pct = pct.Split(' ').Last();
                    double.TryParse(pct, System.Globalization.CultureInfo.InvariantCulture, out idle);
                }
            }
            dto.CpuUtilization = Math.Round(100 - idle, 1);
            break;
        }
    }

    private static string Run(string command, string args)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proc is null) return string.Empty;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }
}
