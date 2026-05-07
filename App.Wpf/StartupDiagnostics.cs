using System.IO;

namespace TitanAILivePC;

/// <summary>Writes to %LocalApplicationData%\TitanAILivePC\startup.log for silent startup failures.</summary>
internal static class StartupDiagnostics
{
    private static readonly object Sync = new();
    private static string? _logPath;

    public static string LogFilePath =>
        _logPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TitanAILivePC",
            "startup.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n";
            lock (Sync)
            {
                File.AppendAllText(LogFilePath, line);
            }
        }
        catch
        {
            // best effort
        }
    }
}
