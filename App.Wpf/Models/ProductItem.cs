namespace TitanAILivePC.Models;

public sealed class ProductItem
{
    public string Category { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional explicit lookup key (ASCII letters+digits semantics). If null, derived from name.</summary>
    public string? NormalizedKey { get; init; }

    public string Unit { get; init; } = "1 cái";
    public decimal Price { get; init; }
    public List<string> Aliases { get; init; } = [];
}
