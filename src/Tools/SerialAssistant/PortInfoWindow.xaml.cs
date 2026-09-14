using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

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
/// Data comes from WMI, so this lives in the tool exe (Windows IO) like SystemSerialTransport.
/// The window opens empty and queries WMI on a background thread (a full Win32_PnPEntity scan
/// takes a second or more); <see cref="RefreshAsync"/> keeps the UI responsive while it runs.
/// Class is public (the XAML-generated partial is), the constructor stays internal.</summary>
public sealed partial class PortInfoWindow : Window
{
    private sealed record DetailRow(string Property, string Value);

    private bool _loading;

    internal PortInfoWindow()
    {
        InitializeComponent();
        _ = RefreshAsync(); // starts before ShowDialog; the continuation lands on the UI thread
    }

    private async Task RefreshAsync()
    {
        if (_loading) return;
        _loading = true;
        RefreshButton.IsEnabled = false;
        LoadingText.Text = "Querying devices…";
        LoadingText.Visibility = Visibility.Visible;
        DevicesList.ItemsSource = null;
        DetailsList.ItemsSource = null;
        SelectButton.IsEnabled = false;
        try
        {
            var entries = await Task.Run(QueryDevices);
            if (IsLoaded) LoadEntries(entries); // skip if the window was closed mid-query
            LoadingText.Visibility = Visibility.Collapsed; // collapse only on success; a failure message stays up
        }
        catch (Exception ex) when (ex is ManagementException or COMException)
        {
            // WMI can fail outright (service disabled, repository corruption);
            // show why the list is empty instead of failing silently.
            LoadingText.Text = $"Device query failed: {ex.Message}";
        }
        finally
        {
            _loading = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private void LoadEntries(IReadOnlyList<PortInfoEntry> entries)
    {
        DevicesList.ItemsSource = entries.OrderBy(e => PortNumber(e.PortName)).ToList();
        if (DevicesList.Items.Count > 0)
            DevicesList.SelectedIndex = 0;
        else
            DetailsList.ItemsSource = null;
    }

    /// <summary>The port picked in the device list, or null; consumed by the owner when
    /// the dialog closes with DialogResult true.</summary>
    internal string? SelectedPort => (DevicesList.SelectedItem as PortInfoEntry)?.PortName;

    private void ShowDetails()
    {
        SelectButton.IsEnabled = DevicesList.SelectedItem is not null;
        if (DevicesList.SelectedItem is not PortInfoEntry entry)
        {
            DetailsList.ItemsSource = null;
            return;
        }
        static string OrDash(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;
        DetailsList.ItemsSource = new[]
        {
            new DetailRow("Caption", OrDash(entry.Caption)),
            new DetailRow("Description", OrDash(entry.Description)),
            new DetailRow("Manufacturer", OrDash(entry.Manufacturer)),
            new DetailRow("Status", OrDash(entry.Status)),
            new DetailRow("Config state", entry.ConfigState),
            new DetailRow("PNP device ID", OrDash(entry.PnpDeviceId)),
        };
    }

    private static int PortNumber(string portName) =>
        int.TryParse(Digits().Match(portName).Value, out int n) ? n : int.MaxValue;

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    /// <summary>Serial devices via WMI: entities whose caption carries a "(COMx)" suffix.
    /// Synchronous on purpose — it runs inside Task.Run from RefreshAsync, never on the UI thread.</summary>
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
            if (!SerialPortNames.TryGetComName(caption, out var comName)) continue;
            list.Add(new PortInfoEntry(
                comName,
                caption,
                entity["Description"]?.ToString() ?? "",
                entity["Manufacturer"]?.ToString() ?? "",
                entity["Status"]?.ToString() ?? "",
                entity["ConfigManagerErrorCode"] is uint code ? code : 0,
                entity["PNPDeviceID"]?.ToString() ?? ""));
        }
        return list;
    }

    private void DevicesList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDetails();

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        if (DevicesList.SelectedItem is PortInfoEntry)
            DialogResult = true; // closes; the owner applies SelectedPort to its port combo
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
}
