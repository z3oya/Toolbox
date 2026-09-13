namespace Toolbox.Core.SerialComm;

public enum SerialDirection { Rx, Tx }

/// <summary>One reception/transmission batch with its arrival time. Raw bytes only — hex/text rendering is a UI concern.</summary>
public sealed record SerialChunk(SerialDirection Direction, DateTimeOffset Timestamp, byte[] Data);
