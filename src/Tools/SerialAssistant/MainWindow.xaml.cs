using System.IO;
using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Toolbox.Core.SerialComm;
using Microsoft.Win32;

namespace Toolbox.Tools.SerialAssistant;

public partial class MainWindow : Window
{
    private const int MaxLogChars = 1_000_000; // trim head beyond this; the editor virtualizes so this only bounds memory
    private const int KeptLogChars = 500_000;

    // macOS palette: RX near-black text, TX accent blue (docs/plans/2026-09-13-serialassistant-macos-theme-design.md).
    private static readonly Brush RxBrush = FrozenBrush(0x1C, 0x1C, 0x1E);
    private static readonly Brush TxBrush = FrozenBrush(0x00, 0x7A, 0xFF);

    private static Brush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze(); // immutable and shareable across render threads
        return brush;
    }

    private readonly DispatcherTimer _uiTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(100), // drains session chunks in batches; the DataReceived thread never touches controls
    };

    private SystemSerialTransport? _transport;
    private SerialSession? _session;
    private TextEncodingKind _encodingKind = TextEncodingKind.Utf8;
    private Decoder _rxDecoder = TextCodec.Resolve(TextEncodingKind.Utf8).GetDecoder(); // stateful: keeps a UTF-8 char split across receive batches intact

    // Color bookkeeping for the log: one entry per document line, sorted by Offset, never
    // crossing a line - keeps the colorizer's binary search and the trim shift exact.
    private readonly List<LogSegment> _segments = new();

    public MainWindow()
    {
        InitializeComponent();
        FillChoices();
        LogEditor.TextArea.TextView.LineTransformers.Add(new DirectionColorizer(_segments));
        AttachEditorMenu(LogEditor, editable: false);
        AttachEditorMenu(TxEditor, editable: true);
        _uiTimer.Tick += (_, _) => DrainAndRender();
        RefreshPorts();
        Loaded += (_, _) =>
        {
            // The option groups must never clip their controls: pin each row's minimum
            // height to the group's fully-measured height instead of a hardcoded estimate.
            ReceiveGroup.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            ReceiveRow.MinHeight = Math.Ceiling(ReceiveGroup.DesiredSize.Height);
            SendGroup.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            SendRow.MinHeight = Math.Ceiling(SendGroup.DesiredSize.Height);
        };
    }

    // Decouples combo labels from values: SelectedItem carries the value itself,
    // so no ComboBox index is ever cast to a Core enum.
    private sealed record Choice<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }

    private static T Selected<T>(ComboBox box) => ((Choice<T>)box.SelectedItem!).Value;

    // AvalonEdit ships no context menu; give both editors the standard editing
    // commands, targeted at their own text area so keyboard focus elsewhere
    // can't route the commands to the wrong editor.
    private static void AttachEditorMenu(ICSharpCode.AvalonEdit.TextEditor editor, bool editable)
    {
        var menu = new ContextMenu();
        if (editable)
            menu.Items.Add(MakeEditorMenuItem(ApplicationCommands.Cut, "Ctrl+X", editor));
        menu.Items.Add(MakeEditorMenuItem(ApplicationCommands.Copy, "Ctrl+C", editor));
        if (editable)
            menu.Items.Add(MakeEditorMenuItem(ApplicationCommands.Paste, "Ctrl+V", editor));
        menu.Items.Add(MakeEditorMenuItem(ApplicationCommands.SelectAll, "Ctrl+A", editor));
        editor.ContextMenu = menu;
    }

    private static MenuItem MakeEditorMenuItem(RoutedCommand command, string gesture, ICSharpCode.AvalonEdit.TextEditor editor) =>
        new()
        {
            Command = command,
            CommandTarget = editor.TextArea,
            InputGestureText = gesture,
        };

    private void FillChoices()
    {
        BaudBox.ItemsSource = new[] { "9600", "19200", "38400", "57600", "115200", "230400", "460800", "921600" };
        BaudBox.Text = "115200"; // editable combo: free text allowed, parsed on connect

        var dataBits = Enumerable.Range(SerialPortConfig.MinDataBits, SerialPortConfig.MaxDataBits - SerialPortConfig.MinDataBits + 1)
            .Select(n => new Choice<int>(n.ToString(), n)).ToArray();
        DataBitsBox.ItemsSource = dataBits;
        DataBitsBox.SelectedItem = dataBits[^1]; // 8

        var stopBits = new[]
        {
            new Choice<SerialStopBits>("1", SerialStopBits.One),
            new Choice<SerialStopBits>("1.5", SerialStopBits.OnePointFive),
            new Choice<SerialStopBits>("2", SerialStopBits.Two),
        };
        StopBitsBox.ItemsSource = stopBits;
        StopBitsBox.SelectedItem = stopBits[0];

        var parity = new[]
        {
            new Choice<SerialParity>("None", SerialParity.None),
            new Choice<SerialParity>("Even", SerialParity.Even),
            new Choice<SerialParity>("Odd", SerialParity.Odd),
            new Choice<SerialParity>("Mark", SerialParity.Mark),
            new Choice<SerialParity>("Space", SerialParity.Space),
        };
        ParityBox.ItemsSource = parity;
        ParityBox.SelectedItem = parity[0];

        var flow = new[]
        {
            new Choice<SerialFlowControl>("None", SerialFlowControl.None),
            new Choice<SerialFlowControl>("RTS-CTS", SerialFlowControl.RtsCts),
            new Choice<SerialFlowControl>("XON-XOFF", SerialFlowControl.XOnXOff),
        };
        FlowBox.ItemsSource = flow;
        FlowBox.SelectedItem = flow[0];

        var eols = new[]
        {
            new Choice<string>("None", ""),
            new Choice<string>("CRLF", "\r\n"),
            new Choice<string>("LF", "\n"),
            new Choice<string>("CR", "\r"),
        };
        EolBox.ItemsSource = eols;
        EolBox.SelectedItem = eols[0];
    }

    private void RefreshPorts()
    {
        var selected = (string?)PortBox.SelectedItem;
        var names = SerialPort.GetPortNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        PortBox.ItemsSource = names;
        if (names.Count == 0) return;
        int i = names.IndexOf(selected ?? "");
        PortBox.SelectedIndex = i >= 0 ? i : 0;
    }

    // Themed replacement for MessageBox: the system dialog cannot pick up the app styles.
    private void ShowMessage(string message, MessageBoxImage severity)
    {
        new MessageWindow { Owner = this, Title = Title, Message = message, Severity = severity }.ShowDialog();
    }

    private void Connect()
    {
        if (PortBox.SelectedItem is null)
        {
            ShowMessage("No port selected.", MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(BaudBox.Text.Trim(), out int baud))
        {
            ShowMessage("Invalid baud rate.", MessageBoxImage.Warning);
            return;
        }

        var config = new SerialPortConfig
        {
            PortName = (string)PortBox.SelectedItem,
            BaudRate = baud,
            DataBits = Selected<int>(DataBitsBox),
            StopBits = Selected<SerialStopBits>(StopBitsBox),
            Parity = Selected<SerialParity>(ParityBox),
            FlowControl = Selected<SerialFlowControl>(FlowBox),
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
            ShowMessage($"Cannot open {config.PortName}: {ex.Message}", MessageBoxImage.Error);
            return;
        }

        _transport = transport;
        _session = session;
        ConnectButton.Content = "Disconnect";
        ConnectedDot.Fill = (Brush)FindResource("GreenBrush");
        StatusInfo.Text = ConnectionSummary(config);
        foreach (var box in new Control[] { PortBox, RefreshButton, BaudBox, DataBitsBox, StopBitsBox, ParityBox, FlowBox })
            box.IsEnabled = false;
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
        StatusCounters.Text = "RX 0 B  TX 0 B";
        StatusInfo.Text = "Not connected";
        ConnectButton.Content = "Connect";
        ConnectedDot.Fill = (Brush)FindResource("SeparatorBrush");
        foreach (var box in new Control[] { PortBox, RefreshButton, BaudBox, DataBitsBox, StopBitsBox, ParityBox, FlowBox })
            box.IsEnabled = true;
    }

    private static string ConnectionSummary(SerialPortConfig c) =>
        $"{c.PortName}  |  {c.BaudRate} bps  |  {c.DataBits}{ParityLetter(c.Parity)}{StopBitsLabel(c.StopBits)}  |  {FlowLabel(c.FlowControl)}";

    private static string ParityLetter(SerialParity parity) => parity switch
    {
        SerialParity.Even => "E",
        SerialParity.Odd => "O",
        SerialParity.Mark => "M",
        SerialParity.Space => "S",
        _ => "N",
    };

    private static string StopBitsLabel(SerialStopBits bits) => bits switch
    {
        SerialStopBits.OnePointFive => "1.5",
        SerialStopBits.Two => "2",
        _ => "1",
    };

    private static string FlowLabel(SerialFlowControl flow) => flow switch
    {
        SerialFlowControl.RtsCts => "RTS/CTS",
        SerialFlowControl.XOnXOff => "XON/XOFF",
        _ => "no flow",
    };

    private void Send()
    {
        var session = _session;
        if (session is null || !session.IsOpen)
        {
            ShowMessage("Not connected.", MessageBoxImage.Warning);
            return;
        }
        try
        {
            if (TxHexCheck.IsChecked == true)
            {
                if (!HexCodec.TryParse(TxEditor.Text, out var bytes, out var error))
                {
                    ShowMessage($"Invalid hex input: {error}", MessageBoxImage.Warning);
                    return;
                }
                session.Send(bytes); // parses-to-empty is a silent no-op, like empty text
            }
            else
            {
                var text = TxEditor.Text + Selected<string>(EolBox);
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
        Dispatcher.BeginInvoke(() => HandlePortFailure(ex));
    }

    private void HandlePortFailure(Exception ex)
    {
        if (_session is null) return; // already torn down (user disconnect or window closed)
        ShowMessage($"Port error: {ex.Message}", MessageBoxImage.Error);
        ClosePort();
    }

    private void DrainAndRender()
    {
        var session = _session;
        if (session is null) return;
        var chunks = session.DrainChunks();
        if (chunks.Count > 0)
        {
            var doc = LogEditor.Document;
            var text = new StringBuilder();
            var ranges = new List<(int Start, int Length, bool IsTx)>(chunks.Count);
            foreach (var chunk in chunks)
            {
                bool tx = chunk.Direction == SerialDirection.Tx;
                string line = RenderChunk(chunk, tx) + "\n"; // direction is conveyed by color only
                line = line.Replace("\r\n", "\n").Replace("\r", "\n"); // TextDocument stores text as-is; single-char breaks keep the measured offsets exact
                ranges.Add((text.Length, line.Length, tx));
                text.Append(line);
            }
            int insertOffset = doc.TextLength;
            doc.Insert(insertOffset, text.ToString()); // one insert per batch: one change notification
            foreach (var (start, length, tx) in ranges)
            {
                // Decoded text embeds CR/LF, so one chunk usually spans several document lines;
                // split at line boundaries so a segment never crosses a line — keeps the
                // colorizer lookup and the trim shift exact.
                int segStart = insertOffset + start;
                int end = segStart + length;
                while (segStart < end)
                {
                    var line = doc.GetLineByOffset(segStart);
                    int segEnd = Math.Min(line.Offset + line.TotalLength, end);
                    _segments.Add(new LogSegment(segStart, segEnd - segStart, tx));
                    segStart = segEnd;
                }
            }
            if (AutoScrollCheck.IsChecked == true) LogEditor.ScrollToEnd();
            TrimLog(doc);
            LogEditor.TextArea.TextView.Redraw(); // colors for freshly visible lines; hidden lines are never built
        }
        StatusCounters.Text = $"RX {session.RxBytes:N0} B  TX {session.TxBytes:N0} B";
    }

    private string RenderChunk(SerialChunk chunk, bool tx) =>
        (tx ? TxHexCheck.IsChecked == true : RxHexCheck.IsChecked == true)
            ? HexCodec.Format(chunk.Data)
            : Decode(chunk, tx);

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

    private void TrimLog(TextDocument doc)
    {
        if (doc.TextLength <= MaxLogChars) return;
        // Cut through the end of the line containing the target offset so a line is never
        // sliced; segments align with line boundaries, so each is fully dropped or shifted.
        var line = doc.GetLineByOffset(doc.TextLength - KeptLogChars);
        int cut = line.Offset + line.TotalLength;
        doc.Remove(0, cut);
        int firstKept = FirstSegmentAtOrAfter(_segments, cut);
        _segments.RemoveRange(0, firstKept);
        for (int i = 0; i < _segments.Count; i++)
            _segments[i] = _segments[i] with { Offset = _segments[i].Offset - cut };
        doc.UndoStack.ClearAll(); // undo records would otherwise pin the trimmed text in memory
    }

    private static int FirstSegmentAtOrAfter(List<LogSegment> segments, int offset)
    {
        int lo = 0, hi = segments.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (segments[mid].Offset < offset) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    private void RebuildDecoder() => _rxDecoder = TextCodec.Resolve(_encodingKind).GetDecoder();

    private void SaveLog()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Text log (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"serial-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            LogStore.WriteText(dlg.FileName, LogEditor.Document.Text); // what you see is what is saved, colors aside
        }
        catch (Exception ex)
        {
            ShowMessage($"Save failed: {ex.Message}", MessageBoxImage.Error);
        }
    }

    private void LoadSendFile()
    {
        var dlg = new OpenFileDialog { Filter = "All files (*.*)|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            TxEditor.Text = TxHexCheck.IsChecked == true
                ? HexCodec.Format(LogStore.ReadBytes(dlg.FileName))
                : LogStore.ReadText(dlg.FileName);
        }
        catch (Exception ex)
        {
            ShowMessage($"Load failed: {ex.Message}", MessageBoxImage.Error);
        }
    }

    // Colors the RX/TX ranges of each visible line by binary-searching the segment table.
    private sealed class DirectionColorizer : DocumentColorizingTransformer
    {
        private readonly List<LogSegment> _segments;

        public DirectionColorizer(List<LogSegment> segments) => _segments = segments;

        protected override void ColorizeLine(DocumentLine line)
        {
            int i = FirstSegmentAtOrAfter(_segments, line.Offset);
            for (; i < _segments.Count && _segments[i].Offset < line.EndOffset; i++)
            {
                var s = _segments[i];
                int start = Math.Max(s.Offset, line.Offset);
                int end = Math.Min(s.Offset + s.Length, line.EndOffset);
                if (end > start)
                    ChangeLinePart(start, end,
                        v => v.TextRunProperties.SetForegroundBrush(s.IsTx ? TxBrush : RxBrush));
            }
        }
    }

    private readonly record struct LogSegment(int Offset, int Length, bool IsTx);

    protected override void OnClosed(EventArgs e)
    {
        ClosePort(); // also stops the UI timer
        base.OnClosed(e);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    private void PortInfo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new PortInfoWindow(PortInfoWindow.QueryDevices()) { Owner = this };
        dlg.ShowDialog();
        if (dlg.DialogResult == true && dlg.SelectedPort is string port)
        {
            RefreshPorts(); // repopulate from the live port list first, then honor the pick
            if (PortBox.Items.Contains(port))
                PortBox.SelectedItem = port;
        }
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) Connect(); else ClosePort();
    }

    private void Send_Click(object sender, RoutedEventArgs e) => Send();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        LogEditor.Document.Text = string.Empty;
        _segments.Clear();
        LogEditor.Document.UndoStack.ClearAll();
        RebuildDecoder(); // text decoded after a clear starts from a clean character boundary
    }

    private void SaveLog_Click(object sender, RoutedEventArgs e) => SaveLog();

    private void LoadFile_Click(object sender, RoutedEventArgs e) => LoadSendFile();

    private void ResetCounters_Click(object sender, RoutedEventArgs e) => _session?.ResetCounters();

    private SettingsWindow? _settings;

    // Non-modal so the encoding applies while watching the log; single instance.
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is { IsLoaded: true })
        {
            _settings.Activate();
            return;
        }
        _settings = new SettingsWindow(_encodingKind);
        _settings.EncodingChanged = kind =>
        {
            _encodingKind = kind;
            RebuildDecoder(); // text received after the switch decodes with the new encoding
        };
        _settings.Closed += (_, _) => _settings = null;
        _settings.Owner = this;
        _settings.Show();
    }

    private void TxEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // HasFlag (not ==) matches the WinForms e.Control semantics: Ctrl+Shift+Enter still sends.
        if (e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Enter)
        {
            Send();
            e.Handled = true; // swallow the newline the editor would otherwise insert
        }
    }
}
