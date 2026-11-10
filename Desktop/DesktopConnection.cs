// SignalR client matching DesktopHub on the server (/remote/hubs/desktop).
// Streams the screen and relays input commands from the viewer.
using System.Runtime.CompilerServices;
using AMS.RemoteMac.Shared;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace AMS.RemoteMac.Desktop;

internal sealed class DesktopConnection : IAsyncDisposable
{
    private readonly ConnectionInfo _info;
    private readonly Guid _sessionId;
    private readonly string _accessKey;
    private readonly string _requesterName;
    private readonly string _orgName;
    private readonly string _orgId;
    private HubConnection? _hub;
    private ScreenCapture? _capture;
    private CancellationTokenSource? _streamCts;

    public DesktopConnection(ConnectionInfo info, Guid sessionId, string accessKey,
        string requesterName, string orgName, string orgId)
    {
        _info = info;
        _sessionId = sessionId;
        _accessKey = accessKey;
        _requesterName = requesterName;
        _orgName = orgName;
        _orgId = orgId;
    }

    public async Task ConnectAndRunAsync(CancellationToken ct)
    {
        var hubUrl = _info.Host.TrimEnd('/');
        if (!hubUrl.EndsWith("/remote", StringComparison.OrdinalIgnoreCase))
            hubUrl += "/remote";
        hubUrl += "/hubs/desktop";

        _hub = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .AddMessagePackProtocol()
            .Build();

        RegisterCallbacks();
        await _hub.StartAsync(ct);

        // Tell the server this desktop process owns the session.
        var result = await _hub.InvokeAsync<RcResult>("ReceiveUnattendedSessionInfo",
            _sessionId, _accessKey, Environment.MachineName, _requesterName, _orgName, ct);

        if (!result.IsSuccess)
        {
            Console.Error.WriteLine($"Session rejected: {result.Reason}");
            return;
        }

        // Tell the server the session is ready for viewers.
        await _hub.InvokeAsync("NotifyRequesterUnattendedReady", ct);
        Console.WriteLine("Desktop session ready, waiting for viewer...");

        // Keep running until cancelled.
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }
    }

    private void RegisterCallbacks()
    {
        // Server → Desktop: a viewer wants the screen.
        _hub!.On<string, string, bool, Guid>("GetScreenCast",
            async (viewerId, requesterName, notifyUser, streamId) =>
            {
                Console.WriteLine($"Screen cast requested by {requesterName} (stream {streamId})");
                await StartStreaming(streamId);
            });

        _hub.On<string, string, bool, Guid>("RequestScreenCast",
            async (viewerId, requesterName, notifyUser, streamId) =>
            {
                Console.WriteLine($"Screen cast request from {requesterName} (stream {streamId})");
                await StartStreaming(streamId);
            });

        // Server → Desktop: input from the viewer (binary DTO wrapper from the browser).
        _hub.On<byte[], string>("SendDtoToClient", (dtoWrapper, viewerConnectionId) =>
        {
            ProcessInputDto(dtoWrapper);
        });

        _hub.On<string>("ViewerDisconnected", viewerId =>
        {
            Console.WriteLine($"Viewer {viewerId} disconnected");
            StopStreaming();
        });

        _hub.On<string>("Disconnect", reason =>
        {
            Console.WriteLine($"Disconnected: {reason}");
            StopStreaming();
        });

        // PromptForAccess: AMS asks whether the user consents. In unattended mode, always accept.
        _hub.On<RemoteControlAccessRequest>("PromptForAccess", request =>
        {
            return Task.FromResult(PromptForAccessResult.Accepted);
        });
    }

    private async Task StartStreaming(Guid streamId)
    {
        StopStreaming();
        _capture = new ScreenCapture();
        _streamCts = new CancellationTokenSource();

        Console.WriteLine($"Streaming screen ({_capture.Width}x{_capture.Height})");

        try
        {
            await _hub!.SendAsync("SendDesktopStream", GenerateFrames(_streamCts.Token), streamId);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Stream error: {ex.Message}");
        }
    }

    private async IAsyncEnumerable<byte[]> GenerateFrames([EnumeratorCancellation] CancellationToken ct)
    {
        if (_capture is null) yield break;

        // Send a full frame first.
        var fullFrame = _capture.CaptureFullFrame();
        if (fullFrame is not null)
            yield return fullFrame;

        // Then send only changed tiles.
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(33, ct); // ~30 FPS cap

            var changed = _capture.CaptureChangedRegions();
            if (changed is not null)
                yield return changed;
        }
    }

    private void StopStreaming()
    {
        _streamCts?.Cancel();
        _streamCts?.Dispose();
        _streamCts = null;
        _capture?.Dispose();
        _capture = null;
    }

    /// <summary>
    /// Processes input DTOs from the viewer. The browser viewer packs mouse/keyboard events as binary blobs
    /// (DtoWrapper) that the desktop process interprets. The format comes from the Remotely viewer JS.
    /// </summary>
    private static void ProcessInputDto(byte[] dto)
    {
        if (dto is null || dto.Length < 1) return;

        // The first byte is the DTO type.
        var type = (DtoType)dto[0];

        try
        {
            switch (type)
            {
                case DtoType.MouseMove when dto.Length >= 17:
                    var mx = BitConverter.ToDouble(dto, 1);
                    var my = BitConverter.ToDouble(dto, 9);
                    InputRelay.MouseAction(mx, my, 0, 0);
                    break;

                case DtoType.MouseDown when dto.Length >= 21:
                    var mdx = BitConverter.ToDouble(dto, 1);
                    var mdy = BitConverter.ToDouble(dto, 9);
                    var mdb = BitConverter.ToInt32(dto, 17);
                    InputRelay.MouseAction(mdx, mdy, mdb, 0);
                    break;

                case DtoType.MouseUp when dto.Length >= 21:
                    var mux = BitConverter.ToDouble(dto, 1);
                    var muy = BitConverter.ToDouble(dto, 9);
                    var mub = BitConverter.ToInt32(dto, 17);
                    InputRelay.MouseAction(mux, muy, mub, 0);
                    break;

                case DtoType.MouseWheel when dto.Length >= 19:
                    var mwx = BitConverter.ToDouble(dto, 1);
                    var mwy = BitConverter.ToDouble(dto, 9);
                    var wheel = BitConverter.ToInt16(dto, 17);
                    InputRelay.MouseAction(mwx, mwy, 0, wheel);
                    break;

                case DtoType.KeyDown when dto.Length >= 2:
                    InputRelay.KeyAction(dto[1], false);
                    break;

                case DtoType.KeyUp when dto.Length >= 2:
                    InputRelay.KeyAction(dto[1], true);
                    break;

                case DtoType.KeyPress when dto.Length >= 3:
                    var ch = BitConverter.ToUInt16(dto, 1);
                    InputRelay.KeyActionUnicode(ch, false);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Input error: {ex.Message}");
        }
    }

    private enum DtoType : byte
    {
        MouseMove = 1,
        MouseDown = 2,
        MouseUp = 3,
        MouseWheel = 4,
        KeyDown = 5,
        KeyUp = 6,
        KeyPress = 7,
        // ... other types the viewer may send
    }

    public async ValueTask DisposeAsync()
    {
        StopStreaming();
        if (_hub is not null) await _hub.DisposeAsync();
    }
}
