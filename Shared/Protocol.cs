// Wire protocol DTOs matching the AMS server (Services/RemoteControl/RemotelyProtocol.cs).
// These must stay byte-compatible: SignalR MessagePack serialises by [DataMember] property name.
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace AMS.RemoteMac.Shared;

[DataContract]
public class DeviceClientDto
{
    [DataMember] public string AgentVersion { get; set; } = string.Empty;
    [DataMember] public double CpuUtilization { get; set; }
    [DataMember] public string CurrentUser { get; set; } = string.Empty;
    [DataMember] public string DeviceName { get; set; } = string.Empty;
    [DataMember] public List<Drive> Drives { get; set; } = new();
    [DataMember] public string ID { get; set; } = string.Empty;
    [DataMember] public bool Is64Bit { get; set; }
    [DataMember] public bool IsOnline { get; set; }
    [DataMember] public string[] MacAddresses { get; set; } = Array.Empty<string>();
    [DataMember] public string OrganizationID { get; set; } = string.Empty;
    [DataMember] public Architecture OSArchitecture { get; set; }
    [DataMember] public string OSDescription { get; set; } = string.Empty;
    [DataMember] public string Platform { get; set; } = string.Empty;
    [DataMember] public int ProcessorCount { get; set; }
    [DataMember] public string PublicIP { get; set; } = string.Empty;
    [DataMember] public double TotalMemory { get; set; }
    [DataMember] public double TotalStorage { get; set; }
    [DataMember] public double UsedMemory { get; set; }
    [DataMember] public double UsedStorage { get; set; }
}

public class Drive
{
    public DriveType DriveType { get; set; }
    public string? RootDirectory { get; set; }
    public string? Name { get; set; }
    public string? DriveFormat { get; set; }
    public double FreeSpace { get; set; }
    public double TotalSize { get; set; }
    public string? VolumeLabel { get; set; }
}

[DataContract]
public class RemoteControlAccessRequest
{
    [DataMember] public string OrganizationName { get; set; } = string.Empty;
    [DataMember] public string RequesterDisplayName { get; set; } = string.Empty;
    [DataMember] public string ViewerConnectionId { get; set; } = string.Empty;
}

[DataContract]
public class RcResult
{
    [DataMember] public bool IsSuccess { get; set; }
    [DataMember] public string Reason { get; set; } = string.Empty;
}

public enum PromptForAccessResult { None, Accepted, Denied, Error, TimedOut }
public enum SessionSwitchReasonEx { ConsoleConnect = 1, ConsoleDisconnect = 2, RemoteConnect = 3, RemoteDisconnect = 4 }
public enum SessionEndReasonsEx { Logoff = 1, SystemShutdown = 2 }
