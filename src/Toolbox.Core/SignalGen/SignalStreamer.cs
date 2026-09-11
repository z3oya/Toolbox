using System.Diagnostics;

namespace Toolbox.Core.SignalGen;

/// <summary>Receives the samples of a just-sent block; the span is only valid during the callback.</summary>
public delegate void BlockSentHandler(ReadOnlySpan<float> samples);

/// <summary>Paces the engine into the transport: every block period renders one block, encodes and sends it.
/// A timer callback may catch up multiple overdue blocks; timing comes from an injectable TimeProvider.
/// Start/Stop are not thread-safe: call them from a single (UI) thread; concurrent Start calls would leak a timer.</summary>
public sealed class SignalStreamer : IDisposable
{
    private readonly SignalEngine _engine;
    private readonly ISampleTransport _transport;
    private readonly TimeProvider _time;
    private readonly float[] _buffer;
    private readonly long _blockPeriodTicks;
    private readonly object _sendLock = new();
    private ITimer? _timer;
    private long _nextDeadline;
    private uint _seq;

    /// <summary>Raised on the sending thread right after a block was sent; the span is only valid during the callback.</summary>
    public event BlockSentHandler? BlockSent;

    public int BlockSize { get; }
    public ulong PacketsSent { get; private set; }
    public ulong BytesSent { get; private set; }

    public SignalStreamer(SignalEngine engine, ISampleTransport transport, TimeProvider? timeProvider = null, int blockSize = 960)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        _engine = engine;
        _transport = transport;
        _time = timeProvider ?? TimeProvider.System;
        BlockSize = blockSize;
        _buffer = new float[blockSize];
        _blockPeriodTicks = (long)Math.Round((double)blockSize * Stopwatch.Frequency / engine.SampleRate);
        // A zero-tick period would wedge the catch-up while-loop (and the overdue resync divide);
        // a tiny block at a low sample rate can round down to zero.
        if (_blockPeriodTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(blockSize), blockSize,
                "Block period rounds to zero ticks; increase blockSize or the sample rate.");
    }

    public void Start()
    {
        if (_timer is not null) return; // already running
        var period = TimeSpan.FromTicks((long)Math.Round(
            (double)_blockPeriodTicks * TimeSpan.TicksPerSecond / Stopwatch.Frequency));
        _nextDeadline = _time.GetTimestamp() + _blockPeriodTicks;
        _timer = _time.CreateTimer(_ => SendDueBlocks(), null, period, period);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        lock (_sendLock) { } // drain: wait out an in-flight SendDueBlocks (Monitor is reentrant, so calling Stop from a BlockSent callback is a harmless no-op)
    }

    public void Dispose() => Stop();

    /// <summary>Renders one block from the engine, encodes and sends it. Public so tests can drive the pipeline directly.</summary>
    public void SendOneBlock()
    {
        lock (_sendLock)
        {
            _engine.Render(_buffer);
            var packet = UdpPacketCodec.Encode(_seq++, (ulong)_time.GetTimestamp(), _buffer);
            _transport.Send(packet);
            PacketsSent++;
            BytesSent += (ulong)packet.Length;
            BlockSent?.Invoke(_buffer);
        }
    }

    private void SendDueBlocks()
    {
        lock (_sendLock)
        {
            if (_timer is null) return; // Stop() ran while this callback was in flight: drop it so a following transport Dispose can't race a send
            long now = _time.GetTimestamp();
            long overdue = (now - _nextDeadline) / _blockPeriodTicks;
            if (overdue > 3) _nextDeadline = now; // drop the backlog (sleep resume, debugger pause), re-lock phase to the wall clock; seq stays monotonic
            while (now >= _nextDeadline)
            {
                _engine.Render(_buffer);
                var packet = UdpPacketCodec.Encode(_seq++, (ulong)now, _buffer);
                _transport.Send(packet);
                PacketsSent++;
                BytesSent += (ulong)packet.Length;
                _nextDeadline += _blockPeriodTicks;
                BlockSent?.Invoke(_buffer);
            }
        }
    }
}
