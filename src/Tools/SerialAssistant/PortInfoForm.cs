using System.Management;
using System.Text.RegularExpressions;

namespace Toolbox.Tools.SerialAssistant;

/// <summary>One serial port device as reported by WMI (Win32_PnPEntity).</summary>
internal sealed record PortInfoEntry(
    string PortName,
    string Caption,
    string Description,
    string Manufacturer,
    string Status,
    uint ConfigManagerErrorCode,
    string PnpDeviceId)
{
    /// <summary>0 means working; anything else is a CM error code worth showing next to the number.</summary>
    public string ConfigState => ConfigManagerErrorCode == 0
        ? "0 (working properly)"
        : ConfigManagerErrorCode.ToString();
}

/// <summary>Serial device browser: COM names on the left, everything else on the right.
/// Data comes from WMI, so this lives in the tool exe (Windows IO) like SystemSerialTransport.</summary>
internal sealed partial class PortInfoForm : Form
{
    private readonly ListView _devices = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        HideSelection = true,
        MultiSelect = false,
    };
    private readonly ListView _details = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
    };
    private readonly Button _refresh = new() { Text = "Refresh", AutoSize = true };
    private readonly Button _close = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };

    public PortInfoForm(IReadOnlyList<PortInfoEntry> entries)
    {
        Text = "Serial Port Devices";
        ClientSize = new Size(740, 420);
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        _devices.Columns.Add("Port", 80);
        _details.Columns.Add("Property", 140);
        _details.Columns.Add("Value", 500);

        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        left.Controls.Add(_devices);

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        right.Controls.Add(_details);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 110,
        };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        buttons.Controls.Add(_close);
        buttons.Controls.Add(_refresh);

        Controls.Add(split);
        Controls.Add(buttons);

        _devices.SelectedIndexChanged += (_, _) => ShowDetails();
        _refresh.Click += (_, _) => LoadEntries(QueryDevices());
        CancelButton = _close;

        LoadEntries(entries);
    }

    private void LoadEntries(IReadOnlyList<PortInfoEntry> entries)
    {
        _devices.BeginUpdate();
        _devices.Items.Clear();
        foreach (var entry in entries.OrderBy(e => PortNumber(e.PortName)))
        {
            var item = new ListViewItem(entry.PortName) { Tag = entry };
            _devices.Items.Add(item);
        }
        _devices.EndUpdate();
        if (_devices.Items.Count > 0)
            _devices.Items[0].Selected = true;
        else
            _details.Items.Clear();
    }

    private void ShowDetails()
    {
        if (_devices.SelectedItems.Count == 0 || _devices.SelectedItems[0].Tag is not PortInfoEntry entry) return;
        _details.BeginUpdate();
        _details.Items.Clear();
        foreach (var (name, value) in new (string, string)[]
        {
            ("Caption", entry.Caption),
            ("Description", entry.Description),
            ("Manufacturer", entry.Manufacturer),
            ("Status", entry.Status),
            ("Config state", entry.ConfigState),
            ("PNP device ID", entry.PnpDeviceId),
        })
        {
            var row = new ListViewItem(name);
            row.SubItems.Add(string.IsNullOrWhiteSpace(value) ? "-" : value);
            _details.Items.Add(row);
        }
        _details.EndUpdate();
    }

    private static int PortNumber(string portName) =>
        int.TryParse(Digits().Match(portName).Value, out int n) ? n : int.MaxValue;

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    /// <summary>Serial devices via WMI: entities whose caption carries a "(COMx)" suffix.
    /// Synchronous by design — a local WMI query takes a few hundred ms for a details dialog.</summary>
    internal static List<PortInfoEntry> QueryDevices()
    {
        var list = new List<PortInfoEntry>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT Caption, Description, Manufacturer, Status, ConfigManagerErrorCode, PNPDeviceID " +
            "FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%'");
        foreach (var raw in searcher.Get())
        {
            if (raw is not ManagementObject entity) continue;
            string caption = entity["Caption"]?.ToString() ?? "";
            var match = ComPortName().Match(caption);
            if (!match.Success) continue;
            list.Add(new PortInfoEntry(
                match.Groups[1].Value,
                caption,
                entity["Description"]?.ToString() ?? "",
                entity["Manufacturer"]?.ToString() ?? "",
                entity["Status"]?.ToString() ?? "",
                entity["ConfigManagerErrorCode"] is uint code ? code : 0,
                entity["PNPDeviceID"]?.ToString() ?? ""));
        }
        return list;
    }

    [GeneratedRegex(@"\((COM\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex ComPortName();
}
