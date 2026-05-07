using System.Windows.Media;

namespace TitanAILivePC.Models;

public sealed class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Severity { get; init; } = "INFO";
    public string Message { get; init; } = string.Empty;
    public Brush SeverityBrush =>
        Severity switch
        {
            "WARN" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5C542")),
            "ERROR" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5A5A")),
            _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5CE1E6"))
        };

    public string TimeText => Timestamp.ToString("HH:mm:ss");
}
