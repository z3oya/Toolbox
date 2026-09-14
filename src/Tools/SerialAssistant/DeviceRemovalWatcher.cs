using System.Management;

namespace Toolbox.Tools.SerialAssistant;

/// <summary>Raises DeviceRemoved when the PnP entity carrying the watched COM port
/// disappears (USB unplug). WMI delivers deletions with up to ~1 s latency (WITHIN
/// clause), so the reactive IO-error path in the transport remains the safety net.</summary>
internal sealed class DeviceRemovalWatcher : IDisposable
{
    private readonly string _portName;
    private ManagementEventWatcher? _watcher;

    // Fired on a WMI callback thread: the subscriber must marshal to the UI thread.
    public event Action<string>? DeviceRemoved;

    public DeviceRemovalWatcher(string portName) => _portName = portName;

    public void Start()
    {
        var watcher = new ManagementEventWatcher(
            new WqlEventQuery(
                "__InstanceDeletionEvent", TimeSpan.FromSeconds(1),
                "TargetInstance ISA 'Win32_PnPEntity'"));
        watcher.EventArrived += OnEventArrived;
        _watcher = watcher;
        watcher.Start();
    }

    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        if (e.NewEvent["TargetInstance"] is not ManagementBaseObject device) return;
        string caption = device["Caption"]?.ToString() ?? "";
        if (SerialPortNames.TryGetComName(caption, out var comName) &&
            string.Equals(comName, _portName, StringComparison.OrdinalIgnoreCase))
            DeviceRemoved?.Invoke(comName);
    }

    public void Dispose()
    {
        var watcher = Interlocked.Exchange(ref _watcher, null);
        if (watcher is null) return;
        try { watcher.EventArrived -= OnEventArrived; } catch { }
        // ManagementEventWatcher.Dispose does NOT stop the subscription (only its
        // finalizer would, and Dispose suppresses it) - Stop must be explicit.
        try { watcher.Stop(); } catch { } // sync COM call: throws if WMI died mid-subscription
        try { watcher.Dispose(); } catch { }
    }
}
