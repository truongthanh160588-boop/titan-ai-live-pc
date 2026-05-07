using System.IO;

namespace TitanAILivePC.Models;

public sealed class AppSettings
{
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string ObsHost { get; set; } = "localhost";
    public int ObsPort { get; set; } = 4455;
    public string ObsPassword { get; set; } = string.Empty;
    public bool IsMuted { get; set; }
    public string VoiceName { get; set; } = "vi-VN-HoaiMyNeural";
    public double VoiceSpeed { get; set; } = 1.0;
    public double VoicePitch { get; set; } = 1.0;
    public string VoiceStylePreset { get; set; } = "Greeting";
    public bool AutoVoiceStylePreset { get; set; } = true;
    public string OverlayBrandName { get; set; } = "TITAN AUDIO VIETNAM";
    public string OverlayBrandFontPreset { get; set; } = "Broadcast Bold";
    public string RemoteWebAppUrl { get; set; } = "https://titan-web-cam.vercel.app";
    public string RemoteSignalingServerUrl { get; set; } = "https://titan-camera-server.onrender.com";

    public static AppSettings Load()
    {
        try
        {
            var path = GetFilePath();
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(path);
            return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var path = GetFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }

    private static string GetFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TitanAILivePC",
            "appsettings.json");
}
