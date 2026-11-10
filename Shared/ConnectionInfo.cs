// Reads ConnectionInfo.json — the file the installer writes when it registers the computer with AMS.
// On macOS: /usr/local/lib/CLT/ConnectionInfo.json (the daemon's install dir).
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AMS.RemoteMac.Shared;

public sealed class ConnectionInfo
{
    [JsonPropertyName("DeviceID")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("Host")]
    public string Host { get; set; } = string.Empty;

    [JsonPropertyName("OrganizationID")]
    public string OrganizationId { get; set; } = string.Empty;

    [JsonPropertyName("ServerVerificationToken")]
    public string ServerVerificationToken { get; set; } = string.Empty;

    public string AmsUrl
    {
        get
        {
            var host = Host.TrimEnd('/');
            if (host.EndsWith("/remote", StringComparison.OrdinalIgnoreCase))
                host = host[..^"/remote".Length];
            return host;
        }
    }

    /// <summary>One of AMS's remote-control hubs: "service" (agent) or "desktop".</summary>
    public string HubUrl(string hub) => $"{AmsUrl}/remote/hubs/{hub}";

    public static ConnectionInfo? Load()
    {
        var paths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ConnectionInfo.json"),
            "/usr/local/lib/CLT/ConnectionInfo.json",
            "/usr/local/lib/Remotely/ConnectionInfo.json",
        };

        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var json = File.ReadAllText(path);
                var info = JsonSerializer.Deserialize<ConnectionInfo>(json);
                if (info is not null && !string.IsNullOrWhiteSpace(info.Host) && !string.IsNullOrWhiteSpace(info.DeviceId))
                    return info;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                continue;
            }
        }
        return null;
    }

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }
}
