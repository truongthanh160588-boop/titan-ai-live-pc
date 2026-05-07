using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LicenseTool;

internal sealed class LicenseGeneratorService
{
    private const string PrivateKeyXml = "<RSAKeyValue><Modulus>97lrBjeWvLdnZrm9TjPSSZ47pF0xTR+mgYAXbiOyVkR/p6LL+Ju8NSYKV8paHRnTgiNLhYriu9ZMB/1hHwY7di+PCPU1W3GvNQDPjKKc20ZNL/97N5Hdkfl8m/X0uxW9anGTBDn5xCyFh5rRA5PaLMNSoZV2PCBk49L3Iyu7hMFB65uDt9QnT2lN8tAPKlqTv2fHFpThYpz96v+lEwFbtu93VLHFHPPAWuZZH/G/2JGN23Da74l/RpZZtUHf23S95SK/1nRXxIdLBvmbpX9DYhmNNQq4XVZ4OZMdO8iJD5amD2MCd4rCWiZCdytkByZLs1RcWSXcWtHYziukFI5iKQ==</Modulus><Exponent>AQAB</Exponent><P>+T0mQzSfxXaOR0KIGQt58ce+yc1UptOoSWB1zPKTk290zAuoF85ZwhyxOISmN8RsGU1IwnUy2qGQk7SPu+1cBwQojbrrScXhhi1AFd+N+EWea0MJIcHEcUrXAovrK0culxdXCEq0bBOCLDUqS7qHy+izqeyfUvEAPpkJ+YsI+8s=</P><Q>/nHAJMGGp+8dNXVtxeQHiGw+G1vTTPr9enDdNORkK9Jmk7vknp2gjdYGg8ODJ23J/7+OjQDEzH+pHduiscD0I1fP5kaL1dbBPJ33k5o14W6x7oLDaXqqOxW/qKHjFuz/zwpuBVmDCIVQFivnnvxrzkZMftoulEJkY0YydoYMg1s=</Q><DP>SJmJVbY0e/5mv1cf8buoD8eRSZMn/1hUAtu4NLTMS/wBV5ZlplmTR7m33bC2AjSTEGO0uAAPiiPZy0yjOaiQT/LkJTS3aMvdP4payoROBG2zEad7N3wLzrxwGOvM2tRnO9euoFmyaHDeUCZEZb61462q9+pXFn/hBFrrzuay/TU=</DP><DQ>DdxAbUAlh6xc2PamnisHxgSvdWoRHpZljG/tfN4cHs79S3rmv0Uy48cO38qcsF8oq8fRihjKn6EsozW9rRUnt20nJBIft+xU5mpsfBvgZ4FSK/3viyVldIaAxDzdU/hhDvQwfhYcLzCj5jFKEr0JWlk1/YsBEo5zTX0bbp1qvRU=</DQ><InverseQ>JEtecH1maQXu2N43Q6/G//R4xHZeAEBxnCh6uMuooD2UGAuESilRog1gx8L7U+HE2wURn6cH4huxECKGh35VWryYB2Xk3D5TQ8MsK8ygoTGZfkTCZqxmcklYwSRsUx0KUerI5b960Os8iN0qr+1xeoQiO6wpHUk50ZIcXpL1zpQ=</InverseQ><D>GyPJQekdLpe5UFvRDZxH0aDwT9WV7SkmiNGLv5lRlHn0slz3k8kcGYaNY4jbzoxhy8QqJftNU97qfYWY+lkoco5LUWPr8JTH6TqpgnLeVHejRTrsOO5WJAP9TujnYwfCtMK0pKXlY0StbGndtFKieHz9tI43Yeb6pqsaQFQuOyDp9TSyUcnCsIUHQzdcSVY3ojqXffeNH8TaLK3pRE6ZQYpt+hyxO1pYolFZBZsCW7dxWKEh0rPTxmQcV9MVYzS6pTBpFnCXkmdLNaDN81SZA10jsXBSaWR7xM4DgNyGnssRFetUQ0LBJgkZ6VHpJIwpPqIq9EGGrkcc2ZiDTk4UMQ==</D></RSAKeyValue>";

    public string Generate(string hardwareId, int days)
    {
        var payload = new LicensePayload
        {
            Product = "TitanAILivePC",
            HardwareId = hardwareId.Trim(),
            IssuedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddDays(days)
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

        using var rsa = RSA.Create();
        rsa.FromXmlString(PrivateKeyXml);
        var signature = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return $"{Base64Url(payloadBytes)}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal sealed class LicensePayload
{
    public string Product { get; set; } = "TitanAILivePC";
    public string HardwareId { get; set; } = string.Empty;
    public DateTime IssuedUtc { get; set; }
    public DateTime? ExpiresUtc { get; set; }
}
