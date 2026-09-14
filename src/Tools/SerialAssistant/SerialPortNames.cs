using System.Text.RegularExpressions;

namespace Toolbox.Tools.SerialAssistant;

/// <summary>Extracts the COM port name from a PnP device caption like "USB-SERIAL CH340 (COM3)".
/// Shared by the device browser (PortInfoWindow) and the unplug watcher.</summary>
internal static partial class SerialPortNames
{
    /// <summary>Case-insensitive on purpose: driver captions vary in casing.</summary>
    public static bool TryGetComName(string caption, out string comName)
    {
        var match = ComPortName().Match(caption);
        comName = match.Success ? match.Groups[1].Value : "";
        return match.Success;
    }

    [GeneratedRegex(@"\((COM\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex ComPortName();
}
