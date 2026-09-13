namespace Toolbox.Core.SerialComm;

/// <summary>Dumb file persistence for logs and send-file loading; callers own path and mode decisions.</summary>
public static class LogStore
{
    public static void AppendText(string path, string text) => File.AppendAllText(path, text);
    public static void WriteText(string path, string text) => File.WriteAllText(path, text);
    public static void AppendBytes(string path, byte[] data) => File.AppendAllBytes(path, data);
    public static byte[] ReadBytes(string path) => File.ReadAllBytes(path);
    public static string ReadText(string path) => File.ReadAllText(path);
}
