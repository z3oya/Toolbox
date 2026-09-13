using System.IO;
using System.IO.Ports;
using Toolbox.Core.SerialComm;

namespace Toolbox.Tools.SerialAssistant;

/// <summary>ISerialTransport adapter over System.IO.Ports. Byte APIs only (no ReadExisting/WriteLine, no BaseStream
/// async) to stay clear of the classic SerialPort deadlocks. A receive failure self-closes the port and raises Error,
/// stopping the DataReceived storm after an unplug; Open/Write failures propagate synchronously to the caller.</summary>
internal sealed class SystemSerialTransport : ISerialTransport
{
    // A stalled driver (flow control paused, half-dead USB adapter) otherwise blocks Write
    // forever - the SerialPort default is InfiniteTimeout.
    private const int MinWriteTimeoutMs = 2_000;
    private const int WriteHeadroomMs = 1_000;

    private SerialPort? _port;
    private int _baudRate;

    public bool IsOpen => _port is { IsOpen: true };

    public event Action<byte[]>? DataReceived;
    public event Action<Exception>? Error;

    public void Open(SerialPortConfig config)
    {
        Close();
        var port = new SerialPort(
            config.PortName,
            config.BaudRate,
            MapParity(config.Parity),
            config.DataBits,
            MapStopBits(config.StopBits))
        {
            Handshake = MapFlowControl(config.FlowControl),
            ReadBufferSize = 64 * 1024,
        };
        _baudRate = Math.Max(1, config.BaudRate); // scales the per-write timeout below
        port.DataReceived += OnDataReceived;
        _port = port; // visible to OnDataReceived before Open() can fire it
        try
        {
            port.Open(); // IOException/UnauthorizedAccessException go to the UI
        }
        catch
        {
            Close();
            throw;
        }
    }

    public void Close()
    {
        var port = Interlocked.Exchange(ref _port, null);
        if (port is null) return;
        try { port.DataReceived -= OnDataReceived; } catch { }
        try { port.Close(); } catch { } // Close() can throw again on an already-dead port
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        var port = _port ?? throw new IOException("The port is not open.");
        var buffer = data.ToArray(); // SerialPort.Write has no span overload
        try
        {
            // Wire-time floor for the payload (10 bits per byte: start + 8 data + stop) plus
            // headroom, so a big send at a low baud rate does not time out spuriously.
            // 64-bit math: the int product would wrap past ~210 KB payloads.
            long timeoutMs = Math.Max(MinWriteTimeoutMs, 10L * buffer.Length * 1000 / _baudRate + WriteHeadroomMs);
            port.WriteTimeout = (int)Math.Min(int.MaxValue, timeoutMs);
            port.Write(buffer, 0, buffer.Length);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The port was self-closed by an RX failure (or the user) while this write raced it.
            throw new IOException("The port is no longer open.", ex);
        }
    }

    public void Dispose() => Close();

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var port = _port;
        if (port is null) return;
        try
        {
            if (e.EventType == SerialData.Eof) return;
            int count = port.BytesToRead;
            if (count <= 0) return;
            var buffer = new byte[count]; // fresh array: the session stores the payload
            int read = port.Read(buffer, 0, count);
            if (read > 0)
                DataReceived?.Invoke(read == count ? buffer : buffer[..read]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Close(); // dead port: stop the event storm, then report once
            Error?.Invoke(ex);
        }
        catch (InvalidOperationException)
        {
            // BytesToRead raced a deliberate Close(); nothing to report
        }
    }

    private static Parity MapParity(SerialParity parity) => parity switch
    {
        SerialParity.None => Parity.None,
        SerialParity.Even => Parity.Even,
        SerialParity.Odd => Parity.Odd,
        SerialParity.Mark => Parity.Mark,
        SerialParity.Space => Parity.Space,
        _ => throw new ArgumentOutOfRangeException(nameof(parity)),
    };

    private static StopBits MapStopBits(SerialStopBits bits) => bits switch
    {
        SerialStopBits.One => StopBits.One,
        SerialStopBits.OnePointFive => StopBits.OnePointFive,
        SerialStopBits.Two => StopBits.Two,
        _ => throw new ArgumentOutOfRangeException(nameof(bits)),
    };

    private static Handshake MapFlowControl(SerialFlowControl flow) => flow switch
    {
        SerialFlowControl.None => Handshake.None,
        SerialFlowControl.RtsCts => Handshake.RequestToSend,
        SerialFlowControl.XOnXOff => Handshake.XOnXOff,
        _ => throw new ArgumentOutOfRangeException(nameof(flow)),
    };
}
