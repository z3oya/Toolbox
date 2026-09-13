using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Text;
using ScintillaNET;
using Toolbox.Core.SerialComm;

namespace Toolbox.Tools.SerialAssistant;

public partial class MainForm : Form
{
    private const int MaxLogChars = 1_000_000; // trim head beyond this; the editor virtualizes so this only bounds memory
    private const int KeptLogChars = 500_000;
    private const int StyleRx = 1; // custom styles on top of Style.Default
    private const int StyleTx = 2;

    private readonly ComboBox _port = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _portInfo = new() { Text = "…", AutoSize = false, Width = 30, Dock = DockStyle.Right }; // device details dialog
    private readonly Button _refresh = new() { Text = "Refresh", AutoSize = true };
    private readonly ComboBox _baud = new() { Width = 140, DropDownStyle = ComboBoxStyle.DropDown }; // editable: exotic rates allowed
    private readonly ComboBox _dataBits = new() { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _stopBits = new() { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _parity = new() { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _flow = new() { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _connect = new() { Text = "▶ Connect", AutoSize = true, Margin = new Padding(0, 12, 3, 0) };

    // Scintilla (Notepad++'s editor core): native WinForms control with a number margin,
    // virtualized rendering and per-range styling — no WPF interop in this tool.
    private readonly Scintilla _editor = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        WrapMode = WrapMode.None,
        LexerName = "null", // no syntax lexing; we style ranges ourselves
    };

    // Compose box: same editor, editable. Scintilla is plain-text only — pasted rich
    // formatting (fonts/colors/HTML) is dropped, the clipboard's Unicode text is kept.
    private readonly Scintilla _txEditor = new()
    {
        Dock = DockStyle.Fill,
        WrapMode = WrapMode.None,
        LexerName = "null",
    };

    // Line index -> sent from us? Drives TX/RX styling; kept in lockstep with the document.
    private readonly List<bool> _lineIsTx = new();

    private readonly CheckBox _rxHex = new() { Text = "RX: HEX", AutoSize = true };
    private readonly CheckBox _txHex = new() { Text = "TX: HEX", AutoSize = true };
    private readonly CheckBox _autoScroll = new() { Text = "Auto-scroll", AutoSize = true, Checked = true };
    private readonly Button _reset = new() { Text = "Reset", AutoSize = true };
    private readonly Button _clear = new() { Text = "Clear", AutoSize = true };

    // True bottom status bar: About button on the left, byte counters right-aligned by the sizing grip.
    private readonly StatusStrip _statusBar = new();
    private readonly ToolStripButton _statusAbout = new() { Text = "About", DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripStatusLabel _statusCounters = new() { Text = "RX 0 B  TX 0 B", Spring = true, TextAlign = ContentAlignment.MiddleRight };

    private readonly ComboBox _eol = new() { Width = 70, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _encoding = new() { Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _saveLog = new() { Text = "Save log…", AutoSize = true };

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
        _eol.Items.AddRange(new object[] { "None", "CRLF", "LF", "CR" }); // index drives EolSuffix
        _eol.SelectedIndex = 0;
        _encoding.Items.AddRange(new object[] { "UTF-8", "ASCII", "Latin1", "GBK" });
        _encoding.SelectedIndex = 0;

        ConfigureEditor(_editor);
        _editor.Styles[StyleRx].ForeColor = Color.MidnightBlue;
        _editor.Styles[StyleTx].ForeColor = Color.Firebrick;
        ConfigureEditor(_txEditor);

        // Right column: connection settings stacked top-down.
        var right = new Panel { Dock = DockStyle.Right, Width = 170, Padding = new Padding(8) };
        var settings = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        // Fixed-width info button docked right of the combo: FlowLayout + AutoSize let the
        // "…" button overflow the 140px row and get clipped, so keep the layout deterministic.
        var portRow = new Panel { Width = 140, Height = 28 };
        portRow.Controls.Add(_port);
        portRow.Controls.Add(_portInfo);
        settings.Controls.AddRange(new Control[]
        {
            FieldLabel("Port"), portRow, _refresh,
            FieldLabel("Baud"), _baud,
            FieldLabel("Data bits"), _dataBits,
            FieldLabel("Stop bits"), _stopBits,
            FieldLabel("Parity"), _parity,
            FieldLabel("Flow control"), _flow,
            _connect,
        });
        right.Controls.Add(settings);

        // Bottom-most strip: line ending, encoding, log saving.
        var strip = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(8, 6, 8, 0), WrapContents = false };
        strip.Controls.AddRange(new Control[]
        {
            new Label { Text = "Line ending", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _eol,
            new Label { Text = "Encoding", AutoSize = true, Padding = new Padding(12, 6, 0, 0) }, _encoding,
            new Label { Text = "", AutoSize = true, Width = 12 }, _saveLog,
        });

        var options = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(8, 4, 8, 0), WrapContents = false };
        options.Controls.AddRange(new Control[] { _rxHex, _txHex, _autoScroll, _reset, _clear });

        var sendButtons = new Panel { Dock = DockStyle.Right, Width = 100, Padding = new Padding(4) };
        _send.Dock = DockStyle.Top;
        _loadFile.Dock = DockStyle.Top;
        sendButtons.Controls.Add(_send);
        sendButtons.Controls.Add(_loadFile);

        var sendPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        sendPanel.Controls.Add(_txEditor);
        sendPanel.Controls.Add(sendButtons);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 150 };
        bottom.Controls.Add(sendPanel);
        bottom.Controls.Add(options);

        // Dock precedence runs in reverse add order: the status bar docks first (full-width,
        // under everything), then the right column spans the remaining height, the strip sits
        // at the bottom of the left region, the send block above it, the log fills the rest.
        _statusAbout.Click += (_, _) => MessageBox.Show(
            "Serial Assistant — serial port debug tool (ASCII/HEX, GBK, logging)\n\nToolbox · .NET 10 WinForms · Scintilla editor",
            "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
        _statusBar.Items.Add(_statusAbout);
        _statusBar.Items.Add(_statusCounters);
        Controls.Add(_editor);
        Controls.Add(bottom);
        Controls.Add(strip);
        Controls.Add(right);
        Controls.Add(_statusBar);

        _refresh.Click += (_, _) => RefreshPorts();
        _portInfo.Click += (_, _) => new PortInfoForm(PortInfoForm.QueryDevices()).ShowDialog(this);
        _connect.Click += (_, _) => { if (_session is null) Connect(); else ClosePort(); };
        _send.Click += (_, _) => Send();
        _txEditor.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter) { Send(); e.Handled = e.SuppressKeyPress = true; }
        };
        _reset.Click += (_, _) => _session?.ResetCounters();
        _clear.Click += (_, _) =>
        {
            _editor.ReadOnly = false;
            _editor.Text = "";
            _editor.ReadOnly = true;
            _lineIsTx.Clear();
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

        static Label FieldLabel(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 10, 3, 0) };
    }

    private static string EolSuffix(int eolIndex) => eolIndex switch
    {
        1 => "\r\n",
        2 => "\n",
        3 => "\r",
        _ => "",
    };

    /// <summary>Shared plain-text look: mono font, line-number margin, no lexer, no wrap.
    /// Scintilla holds plain text only, so pasted rich formatting is silently discarded.</summary>
    private static void ConfigureEditor(Scintilla editor)
    {
        editor.Styles[Style.Default].Font = "Consolas";
        editor.Styles[Style.Default].Size = 10;
        editor.Styles[Style.Default].ForeColor = Color.Black;
        editor.Styles[Style.Default].BackColor = Color.White;
        editor.StyleClearAll(); // propagate the default to every style before any overrides
        editor.Margins[0].Type = MarginType.Number;
        editor.Margins[0].Width = 44;
        editor.Margins[1].Width = 0; // hide the default symbol/folding margin
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
        _statusCounters.Text = "RX 0 B  TX 0 B";
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
                if (!HexCodec.TryParse(_txEditor.Text, out var bytes, out var error))
                {
                    MessageBox.Show($"Invalid hex input: {error}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                session.Send(bytes); // parses-to-empty is a silent no-op, like empty text
            }
            else
            {
                var text = _txEditor.Text + EolSuffix(_eol.SelectedIndex);
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
                _editor.GotoPosition(_editor.TextLength);
            TrimLog();
        }
        _statusCounters.Text = $"RX {session.RxBytes:N0} B  TX {session.TxBytes:N0} B";
    }

    private void AppendChunk(SerialChunk chunk)
    {
        bool tx = chunk.Direction == SerialDirection.Tx;
        string text = (tx ? _txHex.Checked : _rxHex.Checked) ? HexCodec.Format(chunk.Data) : Decode(chunk, tx);
        string line = text + "\n"; // direction is conveyed by color only, no text marker
        int start = _editor.TextLength;
        _editor.ReadOnly = false; // programmatic edits against a read-only view
        _editor.AppendText(line);
        _editor.ReadOnly = true;
        _editor.StartStyling(start);
        _editor.SetStyling(line.Length, tx ? StyleTx : StyleRx);
        _lineIsTx.Add(tx);
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
        if (_editor.TextLength <= MaxLogChars) return;
        // Cut to the next line start so line numbers and the direction list stay in sync.
        int cut = _editor.TextLength - KeptLogChars;
        while (cut < _editor.TextLength && _editor.GetCharAt(cut) != '\n') cut++;
        if (cut < _editor.TextLength) cut++; // past the newline
        int removedLines = _editor.LineFromPosition(cut);
        _editor.ReadOnly = false;
        _editor.DeleteRange(0, cut);
        _editor.ReadOnly = true;
        _lineIsTx.RemoveRange(0, removedLines);
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
            LogStore.WriteText(dlg.FileName, _editor.Text); // what you see is what is saved, colors aside
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
            _txEditor.Text = _txHex.Checked
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
