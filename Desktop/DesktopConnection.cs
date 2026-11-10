// SignalR client of AMS's DesktopHub (/remote/hubs/desktop): streams the screen to the viewer and applies its input.
// The stream is what wwwroot/remote/viewer/src/CaptureProcessor.js reads: frames of [int32 JPEG length][float32 x, y,
// width, height][uint64 timestamp][JPEG], cut into pieces small enough for the hub's 100 KB message limit.
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using AMS.RemoteMac.Shared;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace AMS.RemoteMac.Desktop;

internal sealed class DesktopConnection : IAsyncDisposable
{
    private const int FrameHeaderSize = 28;
    private const int StreamChunkSize = 60_000;
    private const string MainDisplay = "Main display";
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);

    private readonly ConnectionInfo _info;
    private readonly Guid _sessionId;
    private readonly string _accessKey;
    private readonly string _requesterName;
    private readonly string _orgName;
    private readonly string _machineName = MacHost.ComputerName();
    private readonly DtoChannel _dtos = new();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _streamGate = new();
    private HubConnection? _hub;
    private CancellationTokenSource? _streamCts;

    public DesktopConnection(ConnectionInfo info, Guid sessionId, string accessKey, string requesterName, string orgName)
    {
        _info = info;
        _sessionId = sessionId;
        _accessKey = accessKey;
        _requesterName = requesterName;
        _orgName = orgName;
    }

    /// <summary>Runs until AMS ends the session or the connection drops; AMS restarts the process through the agent if needed.</summary>
    public async Task ConnectAndRunAsync(CancellationToken ct)
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(_info.HubUrl("desktop"))
            .AddMessagePackProtocol()
            .Build();
        RegisterCallbacks(_hub);
        _hub.Closed += _ =>
        {
            _closed.TrySetResult();
            return Task.CompletedTask;
        };
        await _hub.StartAsync(ct);

        var result = await _hub.InvokeAsync<RcResult>("ReceiveUnattendedSessionInfo",
            _sessionId, _accessKey, _machineName, _requesterName, _orgName, ct);
        if (!result.IsSuccess)
        {
            Console.Error.WriteLine($"Session rejected: {result.Reason}");
            return;
        }

        await _hub.InvokeAsync("NotifyRequesterUnattendedReady", ct);
        Console.WriteLine("Desktop session ready, waiting for the viewer");
        await _closed.Task.WaitAsync(ct);
    }

    private void RegisterCallbacks(HubConnection hub)
    {
        hub.On<string, string, bool, Guid>("GetScreenCast", (viewerId, _, _, streamId) => _ = StartStreamingAsync(viewerId, streamId));
        hub.On<string, string, bool, Guid>("RequestScreenCast", (viewerId, _, _, streamId) => _ = StartStreamingAsync(viewerId, streamId));

        hub.On<byte[], string>("SendDtoToClient", (wrapper, _) =>
        {
            try
            {
                if (_dtos.Read(wrapper) is { } dto) Apply(dto);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Input error: {ex.Message}");
            }
        });

        hub.On<string>("ViewerDisconnected", viewerId =>
        {
            Console.WriteLine($"Viewer {viewerId} disconnected");
            StopStreaming();
        });

        hub.On<string>("Disconnect", reason =>
        {
            Console.WriteLine($"Disconnected by AMS: {reason}");
            StopStreaming();
            _closed.TrySetResult();
        });

        // The user already agreed by reading the IT Remote Support password to IT; AMS checked it before starting the session.
        hub.On<RemoteControlAccessRequest, PromptForAccessResult>("PromptForAccess", _ => PromptForAccessResult.Accepted);
    }

    private async Task StartStreamingAsync(string viewerId, Guid streamId)
    {
        try
        {
            CancellationTokenSource cts;
            lock (_streamGate)
            {
                _streamCts?.Cancel();
                _streamCts = cts = new CancellationTokenSource();
            }

            var hub = _hub!;
            if (!CoreGraphics.CGPreflightScreenCaptureAccess())
                await hub.InvokeAsync("SendMessageToViewer", viewerId,
                    "This Mac has not allowed Screen Recording for IT Remote Control: only the desktop background shows. Ask the user to click Screen Recording in the IT Remote Support window and turn it on.");
            if (!CoreGraphics.AXIsProcessTrusted())
                await hub.InvokeAsync("SendMessageToViewer", viewerId,
                    "This Mac has not allowed Accessibility for IT Remote Control: you can see the screen but not control it. Ask the user to click Accessibility in the IT Remote Support window and turn it on.");

            var capture = new ScreenCapture();
            var first = capture.CaptureChanges();
            await hub.InvokeAsync("SendDtoToViewer", DtoChannel.Wrap(DtoType.ScreenData, new()
            {
                ["MachineName"] = _machineName,
                ["DisplayNames"] = new[] { MainDisplay },
                ["SelectedDisplay"] = MainDisplay,
                ["ScreenWidth"] = capture.Width,
                ["ScreenHeight"] = capture.Height,
            }), viewerId);

            Console.WriteLine($"Streaming {capture.Width}x{capture.Height} to {viewerId}");
            await hub.SendAsync("SendDesktopStream", Frames(viewerId, capture, first, cts.Token), streamId);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Stream error: {ex.Message}");
        }
    }

    private async IAsyncEnumerable<byte[]> Frames(string viewerId, ScreenCapture capture, Frame? first,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var (width, height) = (capture.Width, capture.Height);
        var frame = first;
        while (!ct.IsCancellationRequested)
        {
            if (frame is not null)
            {
                if (capture.Width != width || capture.Height != height)
                {
                    (width, height) = (capture.Width, capture.Height);
                    await _hub!.InvokeAsync("SendDtoToViewer", DtoChannel.Wrap(DtoType.ScreenSize, new()
                    {
                        ["Width"] = width,
                        ["Height"] = height,
                    }), viewerId, CancellationToken.None);
                }
                var bytes = Encode(frame);
                for (var i = 0; i < bytes.Length; i += StreamChunkSize)
                    yield return bytes[i..Math.Min(bytes.Length, i + StreamChunkSize)];
            }

            try { await Task.Delay(FrameInterval, ct); }
            catch (OperationCanceledException) { }
            if (ct.IsCancellationRequested) break;

            try { frame = capture.CaptureChanges(); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Capture error: {ex.Message}");
                frame = null;
            }
        }
    }

    internal static byte[] Encode(Frame frame)
    {
        var bytes = new byte[FrameHeaderSize + frame.Jpeg.Length];
        var span = bytes.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span, frame.Jpeg.Length);
        BinaryPrimitives.WriteSingleLittleEndian(span[4..], frame.X);
        BinaryPrimitives.WriteSingleLittleEndian(span[8..], frame.Y);
        BinaryPrimitives.WriteSingleLittleEndian(span[12..], frame.Width);
        BinaryPrimitives.WriteSingleLittleEndian(span[16..], frame.Height);
        BinaryPrimitives.WriteInt64LittleEndian(span[20..], DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        frame.Jpeg.CopyTo(span[FrameHeaderSize..]);
        return bytes;
    }

    private static void Apply(Dto dto)
    {
        switch (dto.Type)
        {
            case DtoType.MouseMove:
                InputRelay.MouseMove(dto.Number("PercentX"), dto.Number("PercentY"));
                break;
            case DtoType.MouseDown:
            case DtoType.MouseUp:
                InputRelay.MouseButton((int)dto.Number("Button"), dto.Type == DtoType.MouseDown, dto.Number("PercentX"), dto.Number("PercentY"));
                break;
            case DtoType.Tap:
                InputRelay.MouseMove(dto.Number("PercentX"), dto.Number("PercentY"));
                InputRelay.MouseButton(0, true, dto.Number("PercentX"), dto.Number("PercentY"));
                InputRelay.MouseButton(0, false, dto.Number("PercentX"), dto.Number("PercentY"));
                break;
            case DtoType.MouseWheel:
                InputRelay.Wheel(dto.Number("DeltaX"), dto.Number("DeltaY"));
                break;
            case DtoType.KeyDown:
                InputRelay.KeyDown(dto.Text("Key"));
                break;
            case DtoType.KeyUp:
                InputRelay.KeyUp(dto.Text("Key"));
                break;
            case DtoType.KeyPress:
                InputRelay.KeyPress(dto.Text("Key"));
                break;
            case DtoType.TextTransfer when dto.Flag("TypeText"):
                InputRelay.TypeText(dto.Text("Text"));
                break;
            case DtoType.SetKeyStatesUp:
                InputRelay.ReleaseAll();
                break;
        }
    }

    private void StopStreaming()
    {
        lock (_streamGate)
        {
            _streamCts?.Cancel();
            _streamCts = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        StopStreaming();
        if (_hub is not null) await _hub.DisposeAsync();
    }
}
