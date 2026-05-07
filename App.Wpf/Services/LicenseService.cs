using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using Microsoft.Win32;
using TitanAILivePC.Models;

namespace TitanAILivePC.Services;

public sealed class LicenseService
{
    private const string ProductCode = "TitanAILivePC";
    private const string PublicKeyXml = "<RSAKeyValue><Modulus>97lrBjeWvLdnZrm9TjPSSZ47pF0xTR+mgYAXbiOyVkR/p6LL+Ju8NSYKV8paHRnTgiNLhYriu9ZMB/1hHwY7di+PCPU1W3GvNQDPjKKc20ZNL/97N5Hdkfl8m/X0uxW9anGTBDn5xCyFh5rRA5PaLMNSoZV2PCBk49L3Iyu7hMFB65uDt9QnT2lN8tAPKlqTv2fHFpThYpz96v+lEwFbtu93VLHFHPPAWuZZH/G/2JGN23Da74l/RpZZtUHf23S95SK/1nRXxIdLBvmbpX9DYhmNNQq4XVZ4OZMdO8iJD5amD2MCd4rCWiZCdytkByZLs1RcWSXcWtHYziukFI5iKQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

    public string GetHardwareId()
    {
        var machineGuid = GetMachineGuid();
        var raw = $"{Environment.MachineName}|{Environment.UserDomainName}|{Environment.OSVersion.VersionString}|{Environment.ProcessorCount}|{machineGuid}";
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..24];
    }

    public bool TryValidateStoredLicense(out string error)
    {
        error = string.Empty;
        var path = GetLicensePath();
        if (!File.Exists(path))
        {
            error = "Chưa có license.";
            return false;
        }

        var code = File.ReadAllText(path).Trim();
        return TryValidateActivationCode(code, out _, out error);
    }

    public bool TryActivate(string activationCode, out string error)
    {
        error = string.Empty;
        if (!TryValidateActivationCode(activationCode, out _, out error))
        {
            return false;
        }

        var path = GetLicensePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, activationCode.Trim());
        return true;
    }

    public bool TryValidateActivationCode(string activationCode, out LicensePayload? payload, out string error)
    {
        payload = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(activationCode))
        {
            error = "Activation code trống.";
            return false;
        }

        var parts = activationCode.Trim().Split('.');
        if (parts.Length != 2)
        {
            error = "Activation code không đúng định dạng.";
            return false;
        }

        try
        {
            var payloadBytes = Base64UrlDecode(parts[0]);
            var signature = Base64UrlDecode(parts[1]);
            var payloadJson = Encoding.UTF8.GetString(payloadBytes);
            payload = JsonSerializer.Deserialize<LicensePayload>(payloadJson);
            if (payload is null)
            {
                error = "Không đọc được payload license.";
                return false;
            }

            if (!string.Equals(payload.Product, ProductCode, StringComparison.Ordinal))
            {
                error = "License không thuộc sản phẩm này.";
                return false;
            }

            var localHardwareId = GetHardwareId();
            if (!string.Equals(payload.HardwareId, localHardwareId, StringComparison.OrdinalIgnoreCase))
            {
                error = "License không khớp Hardware ID máy này.";
                return false;
            }

            if (payload.ExpiresUtc.HasValue && payload.ExpiresUtc.Value < DateTime.UtcNow)
            {
                error = "License đã hết hạn.";
                return false;
            }

            using var rsa = RSA.Create();
            rsa.FromXmlString(PublicKeyXml);
            var ok = rsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (!ok)
            {
                error = "Chữ ký license không hợp lệ.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Không thể xác thực license: {ex.Message}";
            return false;
        }
    }

    public static string GetLicensePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TitanAILivePC",
            "license.key");

    private static string GetMachineGuid()
    {
        try
        {
            const string key = @"SOFTWARE\Microsoft\Cryptography";
            var value = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(key)?
                .GetValue("MachineGuid")?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        catch
        {
            // ignore
        }

        return "unknown-machine-guid";
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}
