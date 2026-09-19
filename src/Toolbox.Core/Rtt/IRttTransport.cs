namespace Toolbox.Core.Rtt;

/// <summary>Byte-level bidirectional RTT channel transport. Implementations: J-Link DLL (tool exe), test fakes.
/// DataReceived/Error are raised on a background poll thread; each payload is a fresh array (safe to store).
/// Implementations serialize every native call internally (the J-Link DLL is not thread-safe); callers only
/// need to serialize Open/Close against each other.</summary>
public interface IRttTransport : IDisposable
{
    bool IsOpen { get; }

    /// <summary>Loads the DLL, selects the probe, connects the target and starts RTT;
    /// throws IOException with an actionable message to the caller.</summary>
    void Open(RttConnectionConfig config);

    /// <summary>Idempotent; after an Error event the transport has already closed itself.</summary>
    void Close();

    /// <summary>Throws on failure; a short write (target down-buffer full) warns but does not throw.</summary>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>Raised on a background thread when target bytes arrive.</summary>
    event Action<byte[]>? DataReceived;

    /// <summary>Raised on a background thread when the link failed (probe gone, RTT error); the transport has closed itself.</summary>
    event Action<Exception>? Error;
}
