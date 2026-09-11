using System.Net;
using System.Net.Sockets;
using Toolbox.Core.SignalGen;
using Toolbox.Ui.WinForms.SignalGen;

namespace Toolbox.Tools.FunctionGenerator;

public partial class MainForm : Form
{
    private const int SampleRate = 48_000;
    private const int PreviewBlockSamples = 960; // standalone preview render length (independent of the streamer's block size)

    private readonly List<ChannelConfig> _channels = new();
    private readonly SignalEngine _previewEngine = new(SampleRate);
    private readonly WaveformView _view = new() { Dock = DockStyle.Fill };
    private readonly ChannelEditor _editor = new() { Dock = DockStyle.Fill };
    private readonly ListBox _channelList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox _host = new() { Text = "127.0.0.1", Width = 110 };
    private readonly TextBox _port = new() { Text = "9000", Width = 50 };
    private readonly Button _start = new() { Text = "▶ Send", AutoSize = true };
    private readonly Button _stop = new() { Text = "■ Stop", AutoSize = true, Enabled = false };
    private readonly Button _add = new() { Text = "+ Add", AutoSize = true };
    private readonly Button _remove = new() { Text = "− Remove", AutoSize = true };
    private readonly Label _stats = new() { Text = "idle", AutoSize = true };

    private readonly float[] _previewBuffer = new float[PreviewBlockSamples];
    private float[] _latestBlock = Array.Empty<float>();

    private UdpClientTransport? _transport;
    private SignalEngine? _sendEngine;
    private SignalStreamer? _streamer;
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 33 };

    public MainForm()
    {
        Text = "Function Generator (UDP)";
        ClientSize = new Size(800, 620);

        _channels.Add(new ChannelConfig { Name = "Ch1", Waveform = WaveformKind.Sine, Frequency = 1000, Amplitude = 0.5 });
        _channels.Add(new ChannelConfig { Name = "Ch2", Waveform = WaveformKind.Sine, Frequency = 2000, Amplitude = 0.3, Enabled = false });
        PushChannelsToList();
        SyncEngines();
        _editor.LoadConfig(_channels[0]);
        _channelList.SelectedIndex = 0;

        var scopePanel = new Panel { Dock = DockStyle.Top, Height = 240, Padding = new Padding(4) };
        scopePanel.Controls.Add(_view);

        var left = new Panel { Dock = DockStyle.Left, Width = 220, Padding = new Padding(4) };
        var listButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32, AutoSize = false };
        listButtons.Controls.AddRange(new Control[] { _add, _remove });
        left.Controls.Add(_channelList);
        left.Controls.Add(listButtons);

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        right.Controls.Add(_editor);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8), WrapContents = false };
        bottom.Controls.AddRange(new Control[]
        {
            new Label { Text = "UDP", AutoSize = true, Padding = new Padding(0, 8, 0, 0) }, _host,
            new Label { Text = ":", AutoSize = true, Padding = new Padding(0, 8, 0, 0) }, _port,
            _start, _stop, _stats,
        });

        Controls.Add(right);
        Controls.Add(left);
        Controls.Add(scopePanel);
        Controls.Add(bottom);

        _channelList.SelectedIndexChanged += (_, _) => { if (_channelList.SelectedIndex >= 0) _editor.LoadConfig(_channels[_channelList.SelectedIndex]); };
        _editor.Changed += (_, _) => ApplyEditor();
        _add.Click += (_, _) => { _channels.Add(new ChannelConfig { Name = $"Ch{_channels.Count + 1}" }); PushChannelsToList(); SyncEngines(); _channelList.SelectedIndex = _channels.Count - 1; };
        _remove.Click += (_, _) =>
        {
            int i = _channelList.SelectedIndex;
            if (i < 0 || _channels.Count <= 1) return;
            _channels.RemoveAt(i);
            PushChannelsToList();
            SyncEngines(); // structural change must reach the engines; the selection change only calls LoadConfig (no Changed event)
            _channelList.SelectedIndex = Math.Min(i, _channels.Count - 1);
        };
        _start.Click += (_, _) => StartSending();
        _stop.Click += (_, _) => StopSending();
        _previewTimer.Tick += (_, _) => RenderPreview();
        _previewTimer.Start();
    }

    private void ApplyEditor()
    {
        int i = _channelList.SelectedIndex;
        if (i < 0) return;
        _channels[i] = _editor.Current() with { Name = _channels[i].Name };
        _channelList.Items[i] = ChannelLabel(_channels[i]);
        SyncEngines();
    }

    private void SyncEngines()
    {
        _previewEngine.UpdateChannels(_channels);
        _sendEngine?.UpdateChannels(_channels); // hot-update while streaming (SignalEngine is internally locked)
    }

    private void StartSending()
    {
        // IPv4-only: the transport's socket is IPv4, so a bare IPAddress.TryParse is not enough —
        // it would accept IPv6 literals and die at the first Send.
        if (!IPAddress.TryParse(_host.Text.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            MessageBox.Show("Enter a valid IPv4 address.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!int.TryParse(_port.Text.Trim(), out int port) || port is < 1 or > 65535)
        {
            MessageBox.Show("Invalid port.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        StopSending();
        _latestBlock = Array.Empty<float>(); // the previous session's last block must not paint while stats read "0 pkt / 0 B"
        var engine = new SignalEngine(SampleRate);
        engine.UpdateChannels(_channels);
        _sendEngine = engine;
        _transport = new UdpClientTransport(_host.Text.Trim(), port);
        _streamer = new SignalStreamer(engine, _transport);
        _streamer.BlockSent += block => _latestBlock = block.ToArray(); // sending thread: copy only, span dies with the callback
        _streamer.Start();
        _start.Enabled = false;
        _stop.Enabled = true;
        _stats.Text = "sending…";
    }

    private void StopSending()
    {
        _streamer?.Stop();
        _streamer?.Dispose();
        _streamer = null;
        _sendEngine = null;
        _transport?.Dispose();
        _transport = null;
        _start.Enabled = true;
        _stop.Enabled = false;
        _stats.Text = "idle";
    }

    private void RenderPreview()
    {
        if (_streamer is not null && _latestBlock.Length > 0)
        {
            _view.SetSamples(_latestBlock); // real output from the sending thread
            _stats.Text = $"{_streamer.PacketsSent:N0} pkt / {_streamer.BytesSent:N0} B";
        }
        else
        {
            _previewEngine.Render(_previewBuffer); // standalone preview keeps sender phases untouched
            _view.SetSamples(_previewBuffer);
        }
    }

    private void PushChannelsToList()
    {
        _channelList.Items.Clear();
        foreach (var c in _channels) _channelList.Items.Add(ChannelLabel(c));
    }

    private static string ChannelLabel(ChannelConfig c) =>
        $"{c.Name}: {(c.Enabled ? "" : "(off) ")}{c.Waveform} {c.Frequency:0.##} Hz × {c.Amplitude:0.##}"
            + (c.Mod is { } m ? $" [{m.Kind} {m.ModFrequency:0.##} Hz]" : "");

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopSending();
        _previewTimer.Stop();
        _previewTimer.Dispose(); // Component not parented to any container: dispose explicitly
        base.OnFormClosed(e);
    }
}
