namespace Toolbox.Core.SerialComm;

/// <summary>Owns the open/close lifecycle, byte counters and the RX/TX event queue on top of a transport.
/// DataReceived handlers run on the transport's background thread and only enqueue; the UI thread drains via
/// <see cref="DrainChunks"/>. Timestamps come from an injectable TimeProvider. The session does not own the transport:
/// Close does not Dispose it (creator-owned, matching SignalGen's streamer/transport split).</summary>
public sealed class SerialSession : IDisposable
{
    private readonly ISerialTransport _transport;
    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private readonly Queue<SerialChunk> _pending = new();
    private bool _open;
    private ulong _rxBytes;
    private ulong _txBytes;

    /// <summary>Relayed transport failure (e.g. unplug) raised on a background thread; the UI must marshal.</summary>
    public event EventHandler<Exception>? TransportError;

    public SerialSession(ISerialTransport transport, TimeProvider? timeProvider = null)
    {
        _transport = transport;
        _time = timeProvider ?? TimeProvider.System;
        _transport.DataReceived += OnDataReceived;
        _transport.Error += OnTransportError;
    }

    public bool IsOpen
    {
        get { lock (_lock) return _open; }
    }

    public ulong RxBytes
    {
        get { lock (_lock) return _rxBytes; }
    }

    public ulong TxBytes
    {
        get { lock (_lock) return _txBytes; }
    }

    /// <summary>Opens with config.Clamped(); throws InvalidOperationException when already open.</summary>
    public void Open(SerialPortConfig config)
    {
        lock (_lock)
        {
            if (_open) throw new InvalidOperationException("The port is already open.");
            _transport.Open(config.Clamped());
            _open = true;
        }
    }

    /// <summary>Idempotent. Does not Dispose the transport.</summary>
    public void Close()
    {
        lock (_lock)
        {
            if (!_open) return;
            _open = false;
        }
        _transport.Close(); // outside the lock: Close can block on driver IO
    }

    public void ResetCounters()
    {
        lock (_lock)
        {
            _rxBytes = 0;
            _txBytes = 0;
        }
    }

    /// <summary>Writes raw bytes, counts TX and enqueues the echo chunk; throws InvalidOperationException when closed.
    /// Hex and text sends share this one path; an empty payload writes nothing.</summary>
    public void Send(byte[] data)
    {
        lock (_lock)
        {
            if (!_open) throw new InvalidOperationException("The port is not open.");
            if (data.Length == 0) return;
            _transport.Write(data);
            _txBytes += (ulong)data.Length;
            _pending.Enqueue(new SerialChunk(SerialDirection.Tx, _time.GetUtcNow(), data));
        }
    }

    /// <summary>Encodes and sends; empty text is a no-op.</summary>
    public void SendText(string text, TextEncodingKind encoding)
    {
        if (text.Length == 0) return;
        Send(TextCodec.Resolve(encoding).GetBytes(text));
    }

    /// <summary>Atomically takes every chunk queued since the last drain (UI timer calls this).</summary>
    public List<SerialChunk> DrainChunks()
    {
        lock (_lock)
        {
            if (_pending.Count == 0) return new List<SerialChunk>();
            var chunks = new List<SerialChunk>(_pending.Count);
            while (_pending.Count > 0)
                chunks.Add(_pending.Dequeue());
            return chunks;
        }
    }

    public void Dispose()
    {
        _transport.DataReceived -= OnDataReceived;
        _transport.Error -= OnTransportError;
        Close();
    }

    // Background thread: enqueue and count only — no UI contact, no reentrant transport calls.
    private void OnDataReceived(byte[] data)
    {
        lock (_lock)
        {
            if (!_open || data.Length == 0) return;
            _rxBytes += (ulong)data.Length;
            _pending.Enqueue(new SerialChunk(SerialDirection.Rx, _time.GetUtcNow(), data));
        }
    }

    // Background thread: the transport has closed itself; mark closed so Send/Open fail fast.
    private void OnTransportError(Exception ex)
    {
        lock (_lock) _open = false;
        TransportError?.Invoke(this, ex);
    }
}
