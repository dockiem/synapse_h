namespace SynapseHealth.OrderRouter.Utils;

/// <summary>
/// Parses and evaluates supplier ZIP code coverage from CSV data.
/// Supports three coverage modes: nationwide (full US), explicit individual ZIPs,
/// and numeric ranges (e.g., "10000-10999"). The CSV data is messy — values may
/// contain stray quotes, inconsistent delimiters, and swapped range endpoints.
/// </summary>
public sealed class ZipCoverage
{
    public bool IsNationwide { get; }
    public List<(int Start, int End)> Ranges { get; }
    public HashSet<string> Explicit { get; }

    private ZipCoverage(bool isNationwide, List<(int Start, int End)> ranges, HashSet<string> explicit_)
    {
        IsNationwide = isNationwide;
        Ranges = ranges;
        Explicit = explicit_;
    }

    /// <summary>
    /// Checks if a customer ZIP falls within this supplier's coverage.
    /// Normalizes the ZIP (leading-zero pad), then checks nationwide flag,
    /// explicit set, and numeric ranges in order.
    /// </summary>
    public bool Covers(string zipCode)
    {
        if (IsNationwide) return true;

        var normalized = NormalizeZip(zipCode);
        if (Explicit.Contains(normalized)) return true;

        if (int.TryParse(normalized, out var zipInt))
        {
            foreach (var (start, end) in Ranges)
            {
                if (zipInt >= start && zipInt <= end) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Normalizes a ZIP string to 5 digits. PadLeft handles US ZIPs with leading zeros
    /// (e.g., Boston area "02101") that CSV/Excel may have stripped to "2101".
    /// </summary>
    public static string NormalizeZip(string zip)
    {
        var cleaned = zip.Trim().Trim('"', '\'', ' ');
        return cleaned.PadLeft(5, '0');
    }

    /// <summary>
    /// Parses the raw service_zips CSV value into a ZipCoverage. Handles 5 formats:
    /// explicit lists ("10001, 10002"), single ranges ("10255-10275"), multiple ranges
    /// ("2164-2213, 2143-2193"), nationwide ("00100-99999"), and mixed. Strips stray
    /// quotes and pads all ZIPs to 5 digits.
    /// </summary>
    public static ZipCoverage Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new ZipCoverage(false, [], []);

        var cleaned = raw.Trim().Trim('"');
        var ranges = new List<(int Start, int End)>();
        var explicit_ = new HashSet<string>();
        var isNationwide = false;

        var tokens = cleaned.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            var t = token.Trim().Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(t)) continue;

            var dashIndex = t.IndexOf('-');
            if (dashIndex > 0 && dashIndex < t.Length - 1)
            {
                var startStr = NormalizeZip(t[..dashIndex]);
                var endStr = NormalizeZip(t[(dashIndex + 1)..]);

                if (int.TryParse(startStr, out var start) && int.TryParse(endStr, out var end))
                {
                    // Detect near-full-range spans as nationwide rather than storing a huge range.
                    // This avoids per-ZIP iteration for suppliers that effectively cover everywhere.
                    if (start <= 100 && end >= 99999)
                    {
                        isNationwide = true;
                    }
                    else
                    {
                        // Swap if the CSV has the range endpoints backwards
                        if (start > end) (start, end) = (end, start);
                        ranges.Add((start, end));
                    }
                }
            }
            else
            {
                var normalized = NormalizeZip(t);
                explicit_.Add(normalized);
            }
        }

        return new ZipCoverage(isNationwide, ranges, explicit_);
    }
}
