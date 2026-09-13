namespace Toolbox.Core.SerialComm;

public enum SerialParity { None, Even, Odd, Mark, Space }
public enum SerialStopBits { One, OnePointFive, Two }
public enum SerialFlowControl { None, RtsCts, XOnXOff }

/// <summary>Immutable serial line settings; the tool exe maps these onto System.IO.Ports enums,
/// keeping that package out of Core. An empty <see cref="PortName"/> is a UI validation error, not a clamp.</summary>
public sealed record SerialPortConfig
{
    /// <summary>Lower bound for <see cref="BaudRate"/> (baud).</summary>
    public const int MinBaudRate = 110;
    /// <summary>Upper bound for <see cref="BaudRate"/> (baud).</summary>
    public const int MaxBaudRate = 4_000_000;
    /// <summary>Lower bound for <see cref="DataBits"/>.</summary>
    public const int MinDataBits = 5;
    /// <summary>Upper bound for <see cref="DataBits"/>.</summary>
    public const int MaxDataBits = 8;

    public string PortName { get; init; } = "";
    public int BaudRate { get; init; } = 115200;
    public int DataBits { get; init; } = 8;
    public SerialStopBits StopBits { get; init; } = SerialStopBits.One;
    public SerialParity Parity { get; init; } = SerialParity.None;
    public SerialFlowControl FlowControl { get; init; } = SerialFlowControl.None;

    /// <summary>Returns a copy with BaudRate/DataBits clamped to their valid ranges and PortName trimmed.</summary>
    public SerialPortConfig Clamped() => this with
    {
        PortName = PortName.Trim(),
        BaudRate = Math.Clamp(BaudRate, MinBaudRate, MaxBaudRate),
        DataBits = Math.Clamp(DataBits, MinDataBits, MaxDataBits),
    };
}
