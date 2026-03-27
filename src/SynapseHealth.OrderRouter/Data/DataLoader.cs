using SynapseHealth.OrderRouter.Models;
using SynapseHealth.OrderRouter.Utils;

namespace SynapseHealth.OrderRouter.Data;

/// <summary>
/// Loads supplier and product catalogs from CSV files. Handles real-world data quality
/// issues: typos in column headers (e.g., "suplier_name"), optional trailing punctuation
/// in header names ("can_mail_order?"), unrated suppliers, and duplicate product codes.
/// </summary>
public static class DataLoader
{
    /// <summary>
    /// Reads suppliers.csv, handling column typos, mixed ZIP formats, non-numeric scores,
    /// and inconsistent mail-order flags. Returns cleaned Supplier objects ready for routing.
    /// </summary>
    public static async Task<List<Supplier>> LoadSuppliersAsync(string path, ILogger? logger = null)
    {
        var suppliers = new List<Supplier>();
        var lines = await File.ReadAllLinesAsync(path);
        if (lines.Length == 0) return suppliers;

        var headers = ParseCsvLine(lines[0]);
        var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++)
            colMap[headers[i].Trim()] = i;

        // Try the known typo first ("suplier_name"), fall back to the correct spelling.
        // The CSV source has this typo baked in; fixing it upstream would break other consumers.
        int IdCol(string name) => colMap.GetValueOrDefault(name, -1);
        var idIdx = IdCol("supplier_id");
        var nameIdx = IdCol("suplier_name") is var n and >= 0 ? n : IdCol("supplier_name");
        var zipsIdx = IdCol("service_zips");
        var catsIdx = IdCol("product_categories");
        var scoreIdx = IdCol("customer_satisfaction_score");
        // Column header may or may not include the trailing "?" depending on the export
        var mailIdx = IdCol("can_mail_order?") is var m and >= 0 ? m : IdCol("can_mail_order");

        for (int row = 1; row < lines.Length; row++)
        {
            if (string.IsNullOrWhiteSpace(lines[row])) continue;

            var fields = ParseCsvLine(lines[row]);
            string Get(int idx) => idx >= 0 && idx < fields.Count ? fields[idx] : "";

            var supplierId = Get(idIdx).Trim();
            if (string.IsNullOrEmpty(supplierId)) continue;

            var categories = Get(catsIdx)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => c.ToLowerInvariant())
                .ToHashSet();

            // Score may be numeric or the literal text "no ratings yet" for new suppliers
            double? score = null;
            var scoreStr = Get(scoreIdx).Trim();
            if (!string.IsNullOrEmpty(scoreStr) &&
                !scoreStr.Equals("no ratings yet", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(scoreStr, out var s))
                    score = s;
            }

            var mailStr = Get(mailIdx).Trim();
            var canMail = mailStr.Equals("y", StringComparison.OrdinalIgnoreCase);

            suppliers.Add(new Supplier
            {
                SupplierId = supplierId,
                SupplierName = Get(nameIdx).Trim(),
                ZipCoverage = ZipCoverage.Parse(Get(zipsIdx)),
                ProductCategories = categories,
                SatisfactionScore = score,
                CanMailOrder = canMail
            });
        }

        logger?.LogInformation("Loaded {Count} suppliers", suppliers.Count);
        return suppliers;
    }

    /// <summary>
    /// Reads products.csv into a case-insensitive dictionary. Normalizes categories
    /// to lowercase and deduplicates product codes (keeps first, logs duplicates).
    /// </summary>
    public static async Task<Dictionary<string, Product>> LoadProductsAsync(string path, ILogger? logger = null)
    {
        // Case-insensitive keys so product lookups from order data are forgiving
        var products = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        var lines = await File.ReadAllLinesAsync(path);
        if (lines.Length == 0) return products;

        var headers = ParseCsvLine(lines[0]);
        var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++)
            colMap[headers[i].Trim()] = i;

        var codeIdx = colMap.GetValueOrDefault("product_code", 0);
        var nameIdx = colMap.GetValueOrDefault("product_name", 1);
        var catIdx = colMap.GetValueOrDefault("category", 2);

        var dupes = 0;
        for (int row = 1; row < lines.Length; row++)
        {
            if (string.IsNullOrWhiteSpace(lines[row])) continue;

            var fields = ParseCsvLine(lines[row]);
            if (fields.Count <= codeIdx) continue;

            var code = fields[codeIdx].Trim();
            if (string.IsNullOrEmpty(code)) continue;

            var product = new Product(
                code,
                fields.Count > nameIdx ? fields[nameIdx].Trim() : "",
                (fields.Count > catIdx ? fields[catIdx].Trim() : "").ToLowerInvariant()
            );

            if (!products.TryAdd(code, product))
            {
                dupes++;
                logger?.LogWarning("Duplicate product code '{Code}' at row {Row}, keeping first", code, row + 1);
            }
        }

        if (dupes > 0)
            logger?.LogInformation("Skipped {Count} duplicate product codes", dupes);

        logger?.LogInformation("Loaded {Count} products", products.Count);
        return products;
    }

    /// <summary>
    /// RFC 4180-aware CSV line parser that handles quoted fields with embedded commas.
    /// </summary>
    internal static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
