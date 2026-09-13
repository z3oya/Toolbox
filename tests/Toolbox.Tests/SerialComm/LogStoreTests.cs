namespace Toolbox.Tests.SerialComm;

using Toolbox.Core.SerialComm;

public class LogStoreTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"toolbox-logstore-{Guid.NewGuid():N}.tmp");

    [Fact]
    public void AppendText_then_read_roundtrips()
    {
        var path = TempFile();
        try
        {
            LogStore.AppendText(path, "[12:00:00.000] RX hello\n");
            LogStore.AppendText(path, "[12:00:00.100] TX world\n");
            Assert.Equal("[12:00:00.000] RX hello\n[12:00:00.100] TX world\n", LogStore.ReadText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppendBytes_appends_consecutive_writes()
    {
        var path = TempFile();
        try
        {
            LogStore.AppendBytes(path, new byte[] { 0x01, 0x02 });
            LogStore.AppendBytes(path, new byte[] { 0x03 });
            Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, LogStore.ReadBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadBytes_returns_appended_payload()
    {
        var path = TempFile();
        try
        {
            var payload = new byte[] { 0xAA, 0x55, 0x00, 0xFF };
            LogStore.AppendBytes(path, payload);
            Assert.Equal(payload, LogStore.ReadBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
