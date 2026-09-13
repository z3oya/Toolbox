namespace Toolbox.Core.SerialComm;

/// <summary>Byte-level bidirectional transport. Implementations: SystemSerialTransport (System.IO.Ports, tool exe), test fakes.
/// DataReceived/Error are raised on a background thread; each payload is a fresh array (safe to store).
/// Implementations need not be thread-safe; the session serializes Write under its lock.</summary>
public interface ISerialTransport : IDisposable
{
    bool IsOpen { get; }

    /// <summary>Opens the port; throws IOException/UnauthorizedAccessException (occupied, missing) to the caller.</summary>
    void Open(SerialPortConfig config);

    /// <summary>Idempotent; after an Error event the transport has already closed itself.</summary>
    void Close();

    /// <summary>Throws on failure; the UI layer catches and reports.</summary>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>Raised on a background thread when bytes arrive.</summary>
    event Action<byte[]>? DataReceived;

    /// <summary>Raised on a background thread when the port failed (e.g. unplugged); the transport has closed itself.</summary>
    event Action<Exception>? Error;
}
