using SynapseHealth.OrderRouter.Models;

namespace SynapseHealth.OrderRouter.Services;

/// <summary>
/// Resolves order product codes to catalog entries. Tries exact match first,
/// then falls back to Levenshtein-based fuzzy matching to handle typos in
/// order data (e.g., "WMHC001" vs "WCHM001"). Fuzzy matching can be disabled
/// via strict mode for automated/high-confidence integrations.
/// </summary>
public sealed class ProductMatcher
{
    private readonly Dictionary<string, Product> _products;

    public ProductMatcher(Dictionary<string, Product> products)
    {
        _products = products;
    }

    /// <summary>
    /// Resolves a product code to a catalog entry. Tries exact match, then case-insensitive,
    /// then fuzzy (Levenshtein). In strict mode, only exact/case-insensitive matches are
    /// attempted. Returns the matched product and an optional warning if fuzzy-matched.
    /// </summary>
    public (Product? Product, string? Warning) Match(string productCode, bool strict = false)
    {
        // 1. Exact match
        if (_products.TryGetValue(productCode, out var exact))
            return (exact, null);

        // 2. Case-insensitive (dictionary is already OrdinalIgnoreCase, but handle trimming)
        var trimmed = productCode.Trim();
        if (_products.TryGetValue(trimmed, out var trimMatch))
            return (trimMatch, null);

        // In strict mode, no fuzzy matching — fail immediately
        if (strict)
            return (null, null);

        // 3. Fuzzy match using Levenshtein distance
        var bestMatch = FindClosestMatch(trimmed);
        if (bestMatch != null)
        {
            return (bestMatch, $"Product code '{productCode}' not found exactly; matched to '{bestMatch.ProductCode}'");
        }

        return (null, null);
    }

    /// <summary>
    /// Scans all product codes for the closest Levenshtein match within the 25% edit
    /// distance threshold. Returns null if no code is close enough to avoid false positives.
    /// </summary>
    private Product? FindClosestMatch(string input)
    {
        Product? best = null;
        int bestDistance = int.MaxValue;
        var inputUpper = input.ToUpperInvariant();

        foreach (var (code, product) in _products)
        {
            var codeUpper = code.ToUpperInvariant();
            var distance = LevenshteinDistance(inputUpper, codeUpper);

            // Threshold: allow edits up to 25% of the longer string's length.
            // Tuned empirically — tight enough to avoid false matches between unrelated
            // codes, loose enough to catch single-char typos in typical 6-8 char codes.
            var maxAllowed = Math.Max(1, (int)(Math.Max(inputUpper.Length, codeUpper.Length) * 0.25));

            if (distance < bestDistance && distance <= maxAllowed)
            {
                bestDistance = distance;
                best = product;
            }
        }

        return best;
    }

    /// <summary>
    /// Standard Levenshtein with O(n) space (two-row optimization instead of full matrix).
    /// </summary>
    internal static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++)
            prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }
}
