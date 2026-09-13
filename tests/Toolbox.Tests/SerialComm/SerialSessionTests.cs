namespace Toolbox.Tests.SerialComm;

using System.Text;
using Toolbox.Core.SerialComm;

public class SerialSessionTests
{
    private sealed class FakeTransport : ISerialTransport
    {
        public SerialPortConfig? OpenedWith { get; private set; }
        public List<byte[]> Written { get; } = new();
        public bool IsOpen { get; private set; }

        public event Action<byte[]>? DataReceived;
        public event Action<Exception>? Error;

        public void Open(SerialPortConfig config)
        {
            OpenedWith = config;
            IsOpen = true;
        }

        public void Close() => IsOpen = false;
        public void Write(ReadOnlySpan<byte> data) => Written.Add(data.ToArray());
        public void Dispose() { }

        public void RaiseRx(byte[] data) => DataReceived?.Invoke(data);
        public void RaiseError(Exception ex) => Error?.Invoke(ex);
    }

    /// <summary>Manual clock: the session only reads GetUtcNow, so no timer plumbing is needed.</summary>
    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    [Fact]
    public void Open_passes_clamped_config_and_opens_transport()
    {
        var transport = new FakeTransport();
        using var session = new SerialSession(transport);

        session.Open(new SerialPortConfig { PortName = " COM3 ", BaudRate = 99_999_999 });

        Assert.True(session.IsOpen);
        Assert.Equal(new SerialPortConfig { PortName = "COM3", BaudRate = SerialPortConfig.MaxBaudRate }, transport.OpenedWith);
    }

    [Fact]
    public void Open_when_already_open_throws()
    {
        using var session = new SerialSession(new FakeTransport());
        session.Open(new SerialPortConfig { PortName = "COM3" });
        Assert.Throws<InvalidOperationException>(() => session.Open(new SerialPortConfig { PortName = "COM3" }));
    }

    [Fact]
    public void Send_writes_counts_and_enqueues_tx_chunk()
    {
        var time = new FakeTime();
        var transport = new FakeTransport();
        using var session = new SerialSession(transport, time);
        session.Open(new SerialPortConfig { PortName = "COM3" });

        session.Send(new byte[] { 0x01, 0x02 });

        Assert.Single(transport.Written);
        Assert.Equal(new byte[] { 0x01, 0x02 }, transport.Written[0]);
        Assert.Equal(2UL, session.TxBytes);
        var chunk = Assert.Single(session.DrainChunks());
        Assert.Equal(SerialDirection.Tx, chunk.Direction);
        Assert.Equal(new byte[] { 0x01, 0x02 }, chunk.Data);
        Assert.Equal(time.GetUtcNow(), chunk.Timestamp);
    }

    [Fact]
    public void SendText_encodes_with_selected_encoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // GBK (cp936); idempotent
        var cases = new (TextEncodingKind Kind, string Text, byte[] Expected)[]
        {
            (TextEncodingKind.Utf8, "你好", Encoding.UTF8.GetBytes("你好")),
            (TextEncodingKind.Gbk, "你好", Encoding.GetEncoding(936).GetBytes("你好")),
            (TextEncodingKind.Latin1, "é", Encoding.Latin1.GetBytes("é")),
        };
        foreach (var (kind, text, expected) in cases)
        {
            var transport = new FakeTransport();
            using var session = new SerialSession(transport);
            session.Open(new SerialPortConfig { PortName = "COM3" });

            session.SendText(text, kind);

            Assert.Equal(expected, Assert.Single(transport.Written));
            Assert.Equal((ulong)expected.Length, session.TxBytes);
            Assert.Equal(expected, Assert.Single(session.DrainChunks()).Data);
        }
    }

    [Fact]
    public void SendText_empty_is_noop()
    {
        var transport = new FakeTransport();
        using var session = new SerialSession(transport);
        session.Open(new SerialPortConfig { PortName = "COM3" });

        session.SendText("", TextEncodingKind.Utf8);

        Assert.Empty(transport.Written);
        Assert.Equal(0UL, session.TxBytes);
        Assert.Empty(session.DrainChunks());
    }

    [Fact]
    public void Send_while_closed_throws()
    {
        using var session = new SerialSession(new FakeTransport());
        Assert.Throws<InvalidOperationException>(() => session.Send(new byte[] { 0x01 }));
        Assert.Throws<InvalidOperationException>(() => session.SendText("hi", TextEncodingKind.Utf8));
    }

    [Fact]
    public void DataReceived_enqueues_chunk_with_provider_timestamp_and_counts_rx()
    {
        var time = new FakeTime();
        var transport = new FakeTransport();
        using var session = new SerialSession(transport, time);
        session.Open(new SerialPortConfig { PortName = "COM3" });

        time.Advance(TimeSpan.FromSeconds(5));
        transport.RaiseRx(new byte[] { 0xAA, 0x55 });

        Assert.Equal(2UL, session.RxBytes);
        var chunk = Assert.Single(session.DrainChunks());
        Assert.Equal(SerialDirection.Rx, chunk.Direction);
        Assert.Equal(new byte[] { 0xAA, 0x55 }, chunk.Data);
        Assert.Equal(time.GetUtcNow(), chunk.Timestamp);
    }

    [Fact]
    public void DrainChunks_returns_all_then_clears()
    {
        var transport = new FakeTransport();
        using var session = new SerialSession(transport);
        session.Open(new SerialPortConfig { PortName = "COM3" });

        transport.RaiseRx(new byte[] { 0x01 });
        transport.RaiseRx(new byte[] { 0x02 });
        Assert.Equal(2, session.DrainChunks().Count);
        Assert.Empty(session.DrainChunks());
    }

    [Fact]
    public void DrainChunks_preserves_interleaved_order()
    {
        var transport = new FakeTransport();
        using var session = new SerialSession(transport);
        session.Open(new SerialPortConfig { PortName = "COM3" });

        transport.RaiseRx(new byte[] { 0x01 });
        session.Send(new byte[] { 0x02 });
        transport.RaiseRx(new byte[] { 0x03 });

        Assert.Equal(new[] { SerialDirection.Rx, SerialDirection.Tx, SerialDirection.Rx },
            session.DrainChunks().Select(c => c.Direction).ToArray());
    }

    [Fact]
    public void Close_stops_enqueueing_rx()
    {
        var transport = new FakeTransport();
        using var session = new SerialSession(transport);
        session.Open(new SerialPortConfig { PortName = "COM3" });
        session.Close();

        transport.RaiseRx(new byte[] { 0x01 });

        Assert.False(session.IsOpen);
        Assert.Equal(0UL, session.RxBytes);
        Assert.Empty(session.DrainChunks());
    }

    [Fact]
    public void ResetCounters_zeroes_both()
    {
        var transport = new FakeTransport();
        using var session = new SerialSession(transport);
        session.Open(new SerialPortConfig { PortName = "COM3" });
        session.Send(new byte[] { 0x01 });
        transport.RaiseRx(new byte[] { 0x02, 0x03 });
        Assert.True(session.RxBytes > 0 && session.TxBytes > 0);

        session.ResetCounters();

        Assert.Equal(0UL, session.RxBytes);
        Assert.Equal(0UL, session.TxBytes);
    }

    [Fact]
    public void Transport_error_relays_via_TransportError_event()
    {
        var transport = new FakeTransport();
        using var session = new SerialSession(transport);
        session.Open(new SerialPortConfig { PortName = "COM3" });
        Exception? seen = null;
        session.TransportError += (_, ex) => seen = ex;

        var boom = new IOException("port unplugged");
        transport.RaiseError(boom);

        Assert.Same(boom, seen);
        Assert.False(session.IsOpen);
    }

    [Fact]
    public void Transport_error_while_closed_is_suppressed()
    {
        var transport = new FakeTransport();
        using var session = new SerialSession(transport);
        session.Open(new SerialPortConfig { PortName = "COM3" });
        session.Close(); // deliberate close: a racing in-flight error must not look like a failure
        Exception? seen = null;
        session.TransportError += (_, ex) => seen = ex;

        transport.RaiseError(new IOException("late error after close"));

        Assert.Null(seen);
    }
}
