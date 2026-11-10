// The messages the browser viewer (AMS wwwroot/remote/viewer) exchanges with the desktop process, outside the screen stream.
// Each one is a MessagePack DtoWrapper map {DtoChunk, DtoType, InstanceId, IsFirstChunk, IsLastChunk, RequestId, SequenceId}
// whose DtoChunk pieces, joined in SequenceId order, are the MessagePack map of the DTO itself (DtoChunker.js).
using System.Globalization;
using MessagePack;

namespace AMS.RemoteMac.Desktop;

/// <summary>Values of wwwroot/remote/viewer/src/Enums/DtoType.js.</summary>
internal enum DtoType
{
    ScreenData = 1,
    ScreenSize = 2,
    ClipboardText = 4,
    AudioSample = 5,
    CursorChange = 6,
    SelectScreen = 7,
    MouseMove = 8,
    MouseDown = 9,
    MouseUp = 10,
    Tap = 11,
    MouseWheel = 12,
    KeyDown = 13,
    KeyUp = 14,
    CtrlAltDel = 15,
    ToggleAudio = 17,
    ToggleBlockInput = 18,
    TextTransfer = 19,
    KeyPress = 20,
    File = 22,
    WindowsSessions = 23,
    SetKeyStatesUp = 24,
    FrameReceived = 25,
    OpenFileTransferWindow = 27,
    SessionMetrics = 28,
}

internal sealed class Dto(DtoType type, Dictionary<object, object?> fields)
{
    public DtoType Type { get; } = type;

    public double Number(string name) =>
        fields.TryGetValue(name, out var v) && v is IConvertible c ? c.ToDouble(CultureInfo.InvariantCulture) : 0;

    public string Text(string name) => fields.TryGetValue(name, out var v) ? v as string ?? string.Empty : string.Empty;

    public bool Flag(string name) => fields.TryGetValue(name, out var v) && v is true;
}

internal sealed class DtoChannel
{
    private const int MaxPending = 32;
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);

    private sealed class Pending
    {
        public readonly SortedList<int, byte[]> Parts = new();
        public int? Last;
    }

    private readonly Dictionary<string, Pending> _pending = new();

    /// <summary>The DTO this wrapper completes, or null while more of its chunks are due (or it is not a DTO).</summary>
    public Dto? Read(byte[] wrapperBytes)
    {
        var wrapper = MessagePackSerializer.Deserialize<Dictionary<object, object?>>(wrapperBytes, Options);
        if (!wrapper.TryGetValue("DtoChunk", out var c) || c is not byte[] chunk) return null;
        var type = (DtoType)Convert.ToInt32(wrapper.GetValueOrDefault("DtoType") ?? 0, CultureInfo.InvariantCulture);
        var isLast = wrapper.GetValueOrDefault("IsLastChunk") is true;

        byte[] whole;
        if (wrapper.GetValueOrDefault("IsFirstChunk") is true && isLast)
        {
            whole = chunk;
        }
        else
        {
            var id = wrapper.GetValueOrDefault("InstanceId") as string ?? string.Empty;
            var sequence = Convert.ToInt32(wrapper.GetValueOrDefault("SequenceId") ?? 0, CultureInfo.InvariantCulture);
            if (!_pending.TryGetValue(id, out var pending))
            {
                if (_pending.Count >= MaxPending) _pending.Clear();
                _pending[id] = pending = new Pending();
            }
            pending.Parts[sequence] = chunk;
            if (isLast) pending.Last = sequence;
            // Complete only with every piece 0..Last, whatever order they came in.
            if (pending.Last is not { } last || pending.Parts.Count != last + 1 || pending.Parts.Keys[^1] != last) return null;
            _pending.Remove(id);
            whole = pending.Parts.Values.SelectMany(p => p).ToArray();
        }

        var fields = MessagePackSerializer.Deserialize<Dictionary<object, object?>>(whole, Options);
        return new Dto(type, fields);
    }

    /// <summary>One wrapper carrying the whole DTO: what the desktop sends is small (ScreenData, ScreenSize).</summary>
    public static byte[] Wrap(DtoType type, Dictionary<string, object?> dto) =>
        MessagePackSerializer.Serialize(new Dictionary<string, object?>
        {
            ["DtoChunk"] = MessagePackSerializer.Serialize(dto, Options),
            ["DtoType"] = (int)type,
            ["InstanceId"] = Guid.NewGuid().ToString(),
            ["IsFirstChunk"] = true,
            ["IsLastChunk"] = true,
            ["RequestId"] = null,
            ["SequenceId"] = 0,
        }, Options);
}
