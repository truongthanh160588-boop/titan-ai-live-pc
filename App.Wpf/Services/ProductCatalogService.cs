using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TitanAILivePC.Models;

namespace TitanAILivePC.Services;

public readonly record struct CatalogMatchInfo(bool HasMatch, string? ProductName, bool ExactPrimaryKey, string NormalizedBlob);

public sealed class ProductCatalogService
{
    private static readonly HashSet<string> GenericPricingTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "gia", "bao", "nhieu", "tien", "bao nhieu", "bao gia", "bao nhieu tien", "anh", "chi", "shop"
    };

    private static readonly JsonSerializerOptions ProductJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly List<ProductItem> _products;
    private readonly List<CatalogRow> _rows;

    public bool HasProducts => _products.Count > 0;

    public ProductCatalogService()
    {
        _products = LoadProducts();
        _rows = BuildCatalogRows(_products);
    }

    /// <summary>Giống <see cref="ProductKeyNormalizer.NormalizeProductKey"/> — alias công khai cho VM/log.</summary>
    public static string NormalizeProductKey(string? text) => ProductKeyNormalizer.NormalizeProductKey(text);

    /// <summary>
    /// Match mạnh dùng nội bộ OCR boost: khớp primary normalized hoặc đúng một SKU dài nhất trong blob.
    /// </summary>
    public CatalogMatchInfo EvaluateCatalogMatch(string? viewerText)
    {
        var blob = ProductKeyNormalizer.NormalizeProductKey(viewerText);
        if (blob.Length < 2)
        {
            return new CatalogMatchInfo(false, null, false, blob);
        }

        foreach (var row in _rows)
        {
            if (blob.Equals(row.PrimaryNormalizedKey, StringComparison.Ordinal))
            {
                return new CatalogMatchInfo(true, row.Item.Name, true, blob);
            }
        }

        if (TryGetUniqueLongestKeyHit(blob, out var winRow, out _) && winRow is not null)
        {
            return new CatalogMatchInfo(true, winRow.Item.Name, false, blob);
        }

        return new CatalogMatchInfo(false, null, false, blob);
    }

    public bool TryBuildPriceReply(string? viewerText, out string reply)
    {
        reply = string.Empty;
        if (string.IsNullOrWhiteSpace(viewerText))
        {
            return false;
        }

        var blob = ProductKeyNormalizer.NormalizeProductKey(viewerText);

        if (TryGetUniqueLongestKeyHit(blob, out var uniqueRow, out _) && uniqueRow is not null)
        {
            reply = BuildSinglePriceReply(uniqueRow.Item);
            return true;
        }

        var ranked = RankMatches(viewerText, blob);
        if (ranked.Count == 0)
        {
            return false;
        }

        const int clearWinnerGap = 10;

        if (ranked.Count == 1 ||
            ranked[0].Score - ranked[1].Score >= clearWinnerGap ||
            (ranked[0].Score >= 88 && ranked[1].Score <= 78))
        {
            reply = BuildSinglePriceReply(ranked[0].Item);
            return true;
        }

        var topScore = ranked[0].Score;
        var ambiguous = ranked
            .Where(r => r.Score >= topScore - 6)
            .Take(3)
            .Select(r => r.Item)
            .DistinctBy(p => p.Name)
            .ToList();

        if (ambiguous.Count == 1)
        {
            reply = BuildSinglePriceReply(ambiguous[0]);
            return true;
        }

        reply = BuildAmbiguousPriceReply(ambiguous);
        return true;
    }

    private string BuildSinglePriceReply(ProductItem p)
    {
        return TitanKnowledgeBase.PrefixAssistantName(
            $"{p.Name} hiện giá {FormatPrice(p.Price)}đ / {p.Unit} anh nhé.");
    }

    private static string BuildAmbiguousPriceReply(IReadOnlyList<ProductItem> matches)
    {
        var lines = matches.Select(p => $"- {p.Name}: {FormatPrice(p.Price)}đ / {p.Unit}");
        return TitanKnowledgeBase.PrefixAssistantName("Em thấy anh/chị có thể đang hỏi 1 trong các mẫu sau:\n" +
                                                      string.Join('\n', lines) +
                                                      "\nAnh/chị chốt mã giúp em để báo giá chính xác ngay ạ.");
    }

    private sealed class CatalogRow
    {
        public required ProductItem Item { get; init; }
        public required string PrimaryNormalizedKey { get; init; }
        public required HashSet<string> AllNormalizedKeys { get; init; }
    }

    private static List<CatalogRow> BuildCatalogRows(List<ProductItem> products)
    {
        var rows = new List<CatalogRow>(products.Count);
        foreach (var p in products)
        {
            var primary = !string.IsNullOrWhiteSpace(p.NormalizedKey)
                ? ProductKeyNormalizer.NormalizeProductKey(p.NormalizedKey)
                : GuessPrimaryNormalizedKeyFromName(p.Name);

            if (primary.Length < 3 || ProductKeyNormalizer.IsWeakNumericOnlyKey(primary))
            {
                primary = ProductKeyNormalizer.NormalizeProductKey(p.Name);
            }

            var keys = new HashSet<string>(StringComparer.Ordinal)
            {
                ProductKeyNormalizer.NormalizeProductKey(p.Name)
            };

            if (primary.Length >= 3 && !ProductKeyNormalizer.IsWeakNumericOnlyKey(primary))
            {
                keys.Add(primary);
            }

            foreach (var a in p.Aliases)
            {
                var k = ProductKeyNormalizer.NormalizeProductKey(a);
                if (k.Length >= 3 && !ProductKeyNormalizer.IsWeakNumericOnlyKey(k))
                {
                    keys.Add(k);
                }
            }

            rows.Add(new CatalogRow
            {
                Item = p,
                PrimaryNormalizedKey = primary.Length >= 3 ? primary : ProductKeyNormalizer.NormalizeProductKey(p.Name),
                AllNormalizedKeys = keys
            });
        }

        return rows;
    }

    private static string GuessPrimaryNormalizedKeyFromName(string productName)
    {
        var lower = productName.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(lower.Length);
        foreach (var c in lower)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c is '-' or '.' ? c : ' ');
        }

        var asciiLike = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));

        var matches = Regex.Matches(asciiLike, @"\b[a-z]{1,5}\d[\w\-\.]*\b", RegexOptions.IgnoreCase);
        var best = string.Empty;
        foreach (Match m in matches)
        {
            var k = ProductKeyNormalizer.NormalizeProductKey(m.Value);
            if (k.Length >= best.Length)
            {
                best = k;
            }
        }

        return best.Length >= 3 ? best : ProductKeyNormalizer.NormalizeProductKey(productName);
    }

    private bool TryGetUniqueLongestKeyHit(string blob, out CatalogRow? row, out string matchedKey)
    {
        row = null;
        matchedKey = string.Empty;
        if (blob.Length < 3)
        {
            return false;
        }

        var bestLen = -1;
        var winners = new List<(CatalogRow Row, string Key)>();

        foreach (var r in _rows)
        {
            foreach (var key in r.AllNormalizedKeys)
            {
                if (key.Length < 3 || ProductKeyNormalizer.IsWeakNumericOnlyKey(key))
                {
                    continue;
                }

                if (!blob.Contains(key, StringComparison.Ordinal))
                {
                    continue;
                }

                if (key.Length > bestLen)
                {
                    bestLen = key.Length;
                    winners.Clear();
                    winners.Add((r, key));
                }
                else if (key.Length == bestLen && winners.TrueForAll(w => w.Row.Item.Name != r.Item.Name))
                {
                    winners.Add((r, key));
                }
            }
        }

        if (bestLen < 3 || winners.Count != 1)
        {
            return false;
        }

        row = winners[0].Row;
        matchedKey = winners[0].Key;
        return true;
    }

    private List<(ProductItem Item, int Score)> RankMatches(string query, string blob)
    {
        var normalizedQuery = NormalizeWhitespaceAscii(query);
        var compactQuery = Compact(normalizedQuery);
        var scored = new List<(ProductItem Item, int Score)>();

        foreach (var row in _rows)
        {
            var score = ScoreProductMatch(normalizedQuery, compactQuery, blob, row);
            if (score > 0)
            {
                scored.Add((row.Item, score));
            }
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Item.Name.Length)
            .ToList();
    }

    private static int ScoreProductMatch(string normalizedQuery, string compactQuery, string blob, CatalogRow row)
    {
        var score = 0;
        foreach (var key in row.AllNormalizedKeys)
        {
            if (key.Length < 3 || ProductKeyNormalizer.IsWeakNumericOnlyKey(key))
            {
                continue;
            }

            if (blob.Contains(key, StringComparison.Ordinal))
            {
                score = Math.Max(score, 88 + Math.Min(key.Length, 24));
            }
        }

        var aliasNormalized = row.Item.Aliases.Select(NormalizeWhitespaceAscii).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        var nameNormalized = NormalizeWhitespaceAscii(row.Item.Name);
        aliasNormalized.Add(nameNormalized);

        foreach (var alias in aliasNormalized.Distinct())
        {
            var compactAlias = Compact(alias);
            if (alias.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                score = Math.Max(score, 100);
            }
            else if (!string.IsNullOrWhiteSpace(compactAlias) && compactQuery.Contains(compactAlias, StringComparison.Ordinal))
            {
                score = Math.Max(score, 92);
            }
            else if (normalizedQuery.Contains(alias, StringComparison.OrdinalIgnoreCase))
            {
                score = Math.Max(score, 85);
            }
            else if (alias.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) && normalizedQuery.Length >= 3)
            {
                score = Math.Max(score, 78);
            }
            else
            {
                var overlap = TokenOverlap(normalizedQuery, alias);
                var specificOverlap = TokenOverlapSpecific(normalizedQuery, alias);
                if (overlap >= 2 && specificOverlap >= 1)
                {
                    score = Math.Max(score, 65 + overlap);
                }
            }
        }

        return score;
    }

    private static int TokenOverlap(string left, string right)
    {
        var l = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var r = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return l.Intersect(r).Count();
    }

    private static int TokenOverlapSpecific(string left, string right)
    {
        var l = left.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(IsSpecificToken)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var r = right.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(IsSpecificToken)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return l.Intersect(r).Count();
    }

    private static bool IsSpecificToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (GenericPricingTokens.Contains(token))
        {
            return false;
        }

        // Product-like tokens (contains digits) are strong signals: vf5, t39, t46pro...
        if (token.Any(char.IsDigit))
        {
            return true;
        }

        // Non-generic domain token with enough length can still be useful.
        return token.Length >= 4;
    }

    private static List<ProductItem> LoadProducts()
    {
        foreach (var path in GetProductsFileCandidates())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var json = File.ReadAllText(path);
            var products = JsonSerializer.Deserialize<List<ProductItem>>(json, ProductJsonOptions);
            var sanitized = (products ?? [])
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Where(p => p.Price > 0)
                .Select(p => new ProductItem
                {
                    Category = string.IsNullOrWhiteSpace(p.Category) ? "Khác" : p.Category.Trim(),
                    Name = p.Name.Trim(),
                    NormalizedKey = string.IsNullOrWhiteSpace(p.NormalizedKey) ? null : p.NormalizedKey.Trim(),
                    Unit = string.IsNullOrWhiteSpace(p.Unit) ? "1 cái" : p.Unit.Trim(),
                    Price = p.Price,
                    Aliases = p.Aliases
                        .Where(a => !string.IsNullOrWhiteSpace(a))
                        .Select(a => a.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .ToList();

            if (sanitized.Count > 0)
            {
                return sanitized;
            }
        }

        return [];
    }

    public static List<ProductItem> LoadProductsForEditor() => LoadProducts();

    public static string GetWritableProductsFilePath() => GetProductsFileCandidates().First();

    public static void SaveProductsForEditor(IEnumerable<ProductItem> products)
    {
        var sanitized = (products ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new ProductItem
            {
                Category = string.IsNullOrWhiteSpace(p.Category) ? "Khác" : p.Category.Trim(),
                Name = p.Name.Trim(),
                NormalizedKey = string.IsNullOrWhiteSpace(p.NormalizedKey) ? null : p.NormalizedKey.Trim(),
                Unit = string.IsNullOrWhiteSpace(p.Unit) ? "1 cái" : p.Unit.Trim(),
                Price = p.Price < 0 ? 0 : p.Price,
                Aliases = p.Aliases
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Select(a => a.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .ToList();

        var path = GetWritableProductsFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(sanitized, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    private static IEnumerable<string> GetProductsFileCandidates()
    {
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "products.json"));
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "App.Wpf", "products.json"));
    }

    private static string FormatPrice(decimal price)
    {
        return price.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", ".");
    }

    private static string NormalizeWhitespaceAscii(string text)
    {
        var lowered = text.ToLowerInvariant().Replace('-', ' ').Replace('_', ' ');
        var normalized = lowered.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) ? c : ' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Compact(string text)
    {
        return text.Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}
