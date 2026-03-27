using SynapseHealth.OrderRouter.Utils;

namespace SynapseHealth.OrderRouter.Models;

/// <summary>
/// Represents a supplier loaded from the CSV catalog. Immutable after construction.
/// </summary>
public sealed class Supplier
{
    public required string SupplierId { get; init; }
    public required string SupplierName { get; init; }
    public required ZipCoverage ZipCoverage { get; init; }
    public required HashSet<string> ProductCategories { get; init; }
    public double? SatisfactionScore { get; init; }
    public required bool CanMailOrder { get; init; }

    // Unrated suppliers default to 5.0 (middle of 1-10 scale) so they aren't penalized
    // or unfairly boosted during scoring — they compete neutrally until they earn ratings.
    public double EffectiveScore => SatisfactionScore ?? 5.0;
}
