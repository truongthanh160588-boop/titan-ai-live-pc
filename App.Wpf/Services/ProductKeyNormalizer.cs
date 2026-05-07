using System.Globalization;
using System.Text;

namespace TitanAILivePC.Services;

/// <summary>
/// Chuẩn hóa khóa sản phẩm cho OCR / lookup: chỉ giữ chữ+số, không dấu, không khoảng dấu chấm gạch.
/// </summary>
public static class ProductKeyNormalizer
{
    /// <summary>
    /// lowercase → bỏ dấu tiếng Việt → chỉ giữ [a-z0-9].
    /// Ví dụ: "T-4.16Pro" → "t416pro", "VF 5" → "vf5"
    /// </summary>
    public static string NormalizeProductKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lower = text.ToLowerInvariant();
        var normalized = lower.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                var cl = char.ToLowerInvariant(c);
                if (cl is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
                {
                    sb.Append(cl);
                }
            }
        }

        return sb.ToString();
    }

    public static bool IsWeakNumericOnlyKey(string key)
    {
        return key.Length > 0 && key.Length < 5 && key.All(static c => c is >= '0' and <= '9');
    }
}
