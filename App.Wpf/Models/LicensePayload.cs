namespace TitanAILivePC.Models;

public sealed class LicensePayload
{
    public string Product { get; set; } = "TitanAILivePC";
    public string HardwareId { get; set; } = string.Empty;
    public DateTime IssuedUtc { get; set; }
    public DateTime? ExpiresUtc { get; set; }
}
