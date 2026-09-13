using System.IO.Ports;
using System.Text;
using Toolbox.Core.SerialComm;

namespace Toolbox.Tools.SerialAssistant;

public partial class MainForm : Form
{
    private const int MaxLogChars = 1_000_000; // trim head beyond this; ~1 MB of rendered log is plenty to scroll back through
    private const int KeptLogChars = 500_000;

    private static readonly Color RxColor = Color.MidnightBlue;
    private static readonly Color TxColor = Color.Firebrick;
    private static readonly Font MonoFont = new("Consolas", 9f);

    private readonly ComboBox _port = new() { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _refresh = new() { Text = "Refresh", AutoSize = true };
    private readonly ComboBox _baud = new() { Width = 80, DropDownStyle = ComboBoxStyle.DropDown }; // editable: exotic rates allowed
    private readonly ComboBox _dataBits = new() { Width = 40, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _stopBits = new() { Width = 50, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _parity = new() { Width = 65, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _flow = new() { Width = 95, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _connect = new() { Text = "▶ Connect", AutoSize = true };

    private readonly RichTextBox _rxBox = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BackColor = SystemColors.Window, // ReadOnly alone would gray the box
        Font = MonoFont,
        WordWrap = false,
        DetectUrls = false,
    };

    private readonly CheckBox _rxHex = new() { Text = "RX: HEX", AutoSize = true };
    private readonly CheckBox _txHex = new() { Text = "TX: HEX", AutoSize = true };
    private readonly CheckBox _timestamp = new() { Text = "Timestamp", AutoSize = true, Checked = true };
    private readonly CheckBox _autoScroll = new() { Text = "Auto-scroll", AutoSize = true, Checked = true };
    private readonly CheckBox _crlf = new() { Text = "+CRLF", AutoSize = true };
    private readonly ComboBox _encoding = new() { Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _counters = new() { Text = "RX 0 B  TX 0 B", AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly Button _reset = new() { Text = "Reset", AutoSize = true };
    private readonly Button _clear = new() { Text = "Clear", AutoSize = true };
    private readonly Button _saveLog = new() { Text = "Save log…", AutoSize = true };

    private readonly TextBox _txBox = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        Font = MonoFont,
        WordWrap = false,
        AcceptsReturn = true,
    };
    private readonly Button _send = new() { Text = "Send", AutoSize = true };
    private readonly Button _loadFile = new() { Text = "Load file…", AutoSize = true };

    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 100 }; // drains session chunks in batches; the DataReceived thread never touches controls

    private SystemSerialTransport? _transport;
    private SerialSession? _session;
    private TextEncodingKind _encodingKind = TextEncodingKind.Utf8;
    private Decoder _rxDecoder = TextCodec.Resolve(TextEncodingKind.Utf8).GetDecoder(); // stateful: keeps a UTF-8 char split across receive batches intact

    public MainForm()
    {
        Text = "Serial Assistant";
        ClientSize = new Size(900, 640);

        _baud.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200", "230400", "460800", "921600" });
        _baud.SelectedIndex = 4;
        _dataBits.Items.AddRange(new object[] { "5", "6", "7", "8" });
        _dataBits.SelectedIndex = 3;
        // item index == enum value for the three line-setting combos (orders match the Core enums)
        _stopBits.Items.AddRange(new object[] { "1", "1.5", "2" });
        _stopBits.SelectedIndex = 0;
        _parity.Items.AddRange(new object[] { "None", "Even", "Odd", "Mark", "Space" });
        _parity.SelectedIndex = 0;
        _flow.Items.AddRange(new object[] { "None", "RTS-CTS", "XON-XOFF" });
        _flow.SelectedIndex = 0;
        _encoding.Items.AddRange(new object[] { "UTF-8", "ASCII", "Latin1", "GBK" });
        _encoding.SelectedIndex = 0;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(8, 6, 8, 0), WrapContents = false };
        top.Controls.AddRange(new Control[]
        {
            new Label { Text = "Port", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _port, _refresh,
            new Label { Text = "Baud", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, _baud,
            _dataBits, _stopBits, _parity, _flow, _connect,
        });

        var options = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(8, 4, 8, 0), WrapContents = false };
        options.Controls.AddRange(new Control[] { _rxHex, _txHex, _timestamp, _autoScroll, _crlf, _encoding, _counters, _reset, _clear, _saveLog });

        var sendButtons = new Panel { Dock = DockStyle.Right, Width = 100, Padding = new Padding(4) };
        _send.Dock = DockStyle.Top;
        _loadFile.Dock = DockStyle.Top;
        sendButtons.Controls.Add(_send);
        sendButtons.Controls.Add(_loadFile);

        var sendPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        sendPanel.Controls.Add(_txBox);
        sendPanel.Controls.Add(sendButtons);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 150 };
        bottom.Controls.Add(sendPanel);
        bottom.Controls.Add(options);

        Controls.Add(_rxBox);
        Controls.Add(bottom);
        Controls.Add(top);

        _refresh.Click += (_, _) => RefreshPorts();
        _connect.Click += (_, _) => { if (_session is null) Connect(); else ClosePort(); };
        _send.Click += (_, _) => Send();
        _txBox.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter) { Send(); e.Handled = e.SuppressKeyPress = true; }
        };
        _reset.Click += (_, _) => _session?.ResetCounters();
        _clear.Click += (_, _) =>
        {
            _rxBox.Clear();
            RebuildDecoder(); // text decoded after a clear starts from a clean character boundary
        };
        _saveLog.Click += (_, _) => SaveLog();
        _loadFile.Click += (_, _) => LoadSendFile();
        _encoding.SelectedIndexChanged += (_, _) =>
        {
            _encodingKind = (TextEncodingKind)_encoding.SelectedIndex;
            RebuildDecoder();
        };
        _uiTimer.Tick += (_, _) => DrainAndRender();

        RefreshPorts();
    }

    private void RefreshPorts()
    {
        var selected = _port.Text;
        _port.Items.Clear();
        foreach (var name in SerialPort.GetPortNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            _port.Items.Add(name);
        if (_port.Items.Count == 0) return;
        int i = _port.Items.IndexOf(selected);
        _port.SelectedIndex = i >= 0 ? i : 0;
    }

    private void Connect()
    {
        if (_port.SelectedIndex < 0)
        {
            MessageBox.Show("No port selected.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!int.TryParse(_baud.Text.Trim(), out int baud))
        {
            MessageBox.Show("Invalid baud rate.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var config = new SerialPortConfig
        {
            PortName = _port.Text,
            BaudRate = baud,
            DataBits = _dataBits.SelectedIndex + SerialPortConfig.MinDataBits,
            StopBits = (SerialStopBits)_stopBits.SelectedIndex,
            Parity = (SerialParity)_parity.SelectedIndex,
            FlowControl = (SerialFlowControl)_flow.SelectedIndex,
        };

        var transport = new SystemSerialTransport();
        var session = new SerialSession(transport);
        session.TransportError += OnTransportError; // background thread: marshaled in the handler
        try
        {
            session.Open(config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            session.Dispose();
            transport.Dispose();
            MessageBox.Show($"Cannot open {config.PortName}: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _transport = transport;
        _session = session;
        _connect.Text = "■ Disconnect";
        foreach (var box in new Control[] { _port, _refresh, _baud, _dataBits, _stopBits, _parity, _flow })
            box.Enabled = false;
        _uiTimer.Start();
    }

    private void ClosePort()
    {
        if (_session is null) return;
        _session.TransportError -= OnTransportError; // a racing in-flight error must not pop a box during teardown
        _session.Dispose();
        _transport?.Dispose();
        _session = null;
        _transport = null;
        _uiTimer.Stop();
        _counters.Text = "RX 0 B  TX 0 B";
        _connect.Text = "▶ Connect";
        foreach (var box in new Control[] { _port, _refresh, _baud, _dataBits, _stopBits, _parity, _flow })
            box.Enabled = true;
    }

    private void Send()
    {
        var session = _session;
        if (session is null || !session.IsOpen)
        {
            MessageBox.Show("Not connected.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            if (_txHex.Checked)
            {
                if (!HexCodec.TryParse(_txBox.Text, out var bytes, out var error))
                {
                    MessageBox.Show($"Invalid hex input: {error}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                session.Send(bytes); // parses-to-empty is a silent no-op, like empty text
            }
            else
            {
                var text = _txBox.Text;
                if (_crlf.Checked) text += "\r\n";
                session.SendText(text, _encodingKind);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HandlePortFailure(ex); // the write hit a dead port: same treatment as a receive-side failure
        }
    }

    // TransportError arrives on a background thread — the UI part must run on the UI thread.
    private void OnTransportError(object? sender, Exception ex)
    {
        try { BeginInvoke(() => HandlePortFailure(ex)); }
        catch (ObjectDisposedException) { } // form already gone; OnFormClosed has torn the port down
    }

    private void HandlePortFailure(Exception ex)
    {
        if (IsDisposed) return;
        MessageBox.Show($"Port error: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        ClosePort();
    }

    private void DrainAndRender()
    {
        var session = _session;
        if (session is null) return;
        var chunks = session.DrainChunks();
        if (chunks.Count > 0)
        {
            foreach (var chunk in chunks)
                AppendChunk(chunk);
            if (_autoScroll.Checked)
            {
                _rxBox.SelectionStart = _rxBox.TextLength;
                _rxBox.ScrollToCaret();
            }
            TrimLog();
        }
        _counters.Text = $"RX {session.RxBytes:N0} B  TX {session.TxBytes:N0} B";
    }

    private void AppendChunk(SerialChunk chunk)
    {
        bool tx = chunk.Direction == SerialDirection.Tx;
        bool hex = tx ? _txHex.Checked : _rxHex.Checked;
        string text = hex ? HexCodec.Format(chunk.Data) : Decode(chunk, tx);
        string line = (_timestamp.Checked ? $"[{chunk.Timestamp.LocalDateTime:HH:mm:ss.fff}] " : "")
            + (tx ? "TX " : "RX ") + text + "\n";

        _rxBox.SelectionStart = _rxBox.TextLength;
        _rxBox.SelectionLength = 0;
        _rxBox.SelectionColor = tx ? TxColor : RxColor;
        _rxBox.AppendText(line);
        _rxBox.SelectionColor = _rxBox.ForeColor; // reset so user selections keep the default color
    }

    // RX text keeps a stateful decoder across batches (a UTF-8 char may span two receive events);
    // TX chunks are complete messages by construction, so GetString is enough.
    private string Decode(SerialChunk chunk, bool tx)
    {
        if (tx)
            return TextCodec.Resolve(_encodingKind).GetString(chunk.Data);
        var chars = new char[chunk.Data.Length]; // every offered encoding yields at most one char per byte
        int n = _rxDecoder.GetChars(chunk.Data, 0, chunk.Data.Length, chars, 0);
        return new string(chars, 0, n);
    }

    private void TrimLog()
    {
        if (_rxBox.TextLength <= MaxLogChars) return;
        _rxBox.ReadOnly = false; // ReadOnly blocks clearing SelectedText
        _rxBox.Select(0, _rxBox.TextLength - KeptLogChars);
        _rxBox.SelectedText = "";
        _rxBox.SelectionStart = _rxBox.TextLength;
        _rxBox.ReadOnly = true;
    }

    private void RebuildDecoder() => _rxDecoder = TextCodec.Resolve(_encodingKind).GetDecoder();

    private void SaveLog()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "Text log (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"serial-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            LogStore.WriteText(dlg.FileName, _rxBox.Text); // what you see is what is saved, colors aside
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadSendFile()
    {
        using var dlg = new OpenFileDialog { Filter = "All files (*.*)|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _txBox.Text = _txHex.Checked
                ? HexCodec.Format(LogStore.ReadBytes(dlg.FileName))
                : LogStore.ReadText(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Load failed: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        ClosePort();
        _uiTimer.Stop();
        _uiTimer.Dispose(); // Component not parented to any container: dispose explicitly
        base.OnFormClosed(e);
    }
}
