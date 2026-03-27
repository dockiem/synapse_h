using SynapseHealth.OrderRouter.Models;

namespace SynapseHealth.OrderRouter.Services;

/// <summary>
/// Core routing engine. For each order, resolves products, finds eligible suppliers
/// per item, then uses a greedy set-cover algorithm to minimize the number of
/// suppliers while preferring higher-quality and local ones. A greedy approach is
/// used instead of brute-force because the optimal set cover is NP-hard, and the
/// greedy heuristic gives a good-enough O(n*m) solution for our supplier/item counts.
/// </summary>
public sealed class OrderRouterService : IOrderRouter
{
    private readonly List<Supplier> _suppliers;
    private readonly ProductMatcher _matcher;
    private readonly ILogger<OrderRouterService> _logger;

    // Scoring weights for the greedy supplier selection. Consolidation is weighted
    // heavily (10x) because fewer shipments = lower cost and simpler logistics.
    // Quality (satisfaction score) is the secondary factor — even a slight edge
    // in rating wins over local preference. LocalBonus is near-zero so it only
    // breaks ties when satisfaction scores are identical (per spec: "prefer local
    // over mail-order when ratings are similar").
    private const double ConsolidationWeight = 10.0;
    private const double QualityWeight = 1.0;
    private const double LocalBonus = 0.01;

    public OrderRouterService(
        List<Supplier> suppliers,
        Dictionary<string, Product> products,
        ILogger<OrderRouterService> logger)
    {
        _suppliers = suppliers;
        _matcher = new ProductMatcher(products);
        _logger = logger;
    }

    /// <summary>
    /// Routes a single order to the best supplier(s). Validates the order, resolves product
    /// codes (with optional fuzzy matching), finds eligible suppliers per item based on
    /// category + ZIP coverage, then runs a greedy set-cover to minimize shipments.
    /// Returns a RouteResponse with routing assignments or error details.
    /// </summary>
    public RouteResponse Route(OrderRequest order)
    {
        // Step 1: Validate
        var errors = Validate(order);
        if (errors.Count > 0)
            return RouteResponse.Failure(order.OrderId ?? "unknown", errors);

        // Step 2: Resolve items to categories
        var warnings = new List<string>();
        var resolvedItems = new List<(OrderItemRequest Item, Product Product)>();

        foreach (var item in order.Items!)
        {
            var (product, warning) = _matcher.Match(item.ProductCode ?? "", order.Strict);
            if (warning != null) warnings.Add(warning);

            if (product == null)
            {
                errors.Add($"Unknown product code: '{item.ProductCode}'");
                continue;
            }
            resolvedItems.Add((item, product));
        }

        if (errors.Count > 0)
            return RouteResponse.Failure(order.OrderId ?? "unknown", errors);

        // Step 3: Find eligible suppliers per item
        var customerZip = Utils.ZipCoverage.NormalizeZip(order.CustomerZip!);
        var eligiblePerItem = new List<(OrderItemRequest Item, Product Product, List<EligibleSupplier> Suppliers)>();

        foreach (var (item, product) in resolvedItems)
        {
            var eligible = FindEligibleSuppliers(product.Category, customerZip, order.MailOrder, order.Strict);
            if (eligible.Count == 0)
            {
                errors.Add($"No supplier available for '{product.ProductCode}' (category: {product.Category}) in ZIP {customerZip}");
                continue;
            }
            eligiblePerItem.Add((item, product, eligible));
        }

        if (errors.Count > 0)
            return RouteResponse.Failure(order.OrderId ?? "unknown", errors);

        // Warn when no supplier explicitly lists this ZIP in their service area.
        // The order still gets routed via nationwide suppliers, but ops should know
        // in case it indicates a gap in local supplier coverage.
        if (!order.Strict)
        {
            var hasSpecificLocalSupplier = _suppliers.Any(s =>
                !s.ZipCoverage.IsNationwide && s.ZipCoverage.Covers(customerZip));
            if (!hasSpecificLocalSupplier)
            {
                warnings.Add($"ZIP code '{customerZip}' is not in any supplier's specific service area; routed via nationwide supplier(s) only.");
            }
        }

        // Step 4: Greedy set cover assignment
        var assignments = GreedyAssign(eligiblePerItem, customerZip, order.MailOrder);

        // Warn about nationwide suppliers in the assignment. Two cases:
        // 1. Nationwide + no mail order = likely a distributor with their own delivery network,
        //    ops should verify local delivery availability for this specific ZIP
        // 2. Nationwide + items that NO specific local supplier could cover = coverage gap
        if (!order.Strict)
        {
            foreach (var s in assignments.Select(a => a.Supplier).Distinct())
            {
                if (!s.ZipCoverage.IsNationwide) continue;

                if (!s.CanMailOrder)
                {
                    warnings.Add($"Supplier '{s.SupplierId}' ({s.SupplierName}) is a nationwide distributor (no direct mail order) — verify local delivery availability for ZIP {customerZip}.");
                    continue;
                }

                // Check if the items assigned to this nationwide supplier could have been
                // covered by any non-nationwide supplier — if not, it's a real coverage gap
                var assignedCategories = assignments
                    .Where(a => a.Supplier.SupplierId == s.SupplierId)
                    .Select(a => a.Product.Category)
                    .ToHashSet();

                var localCanCover = assignedCategories.All(cat =>
                    _suppliers.Any(ls => !ls.ZipCoverage.IsNationwide
                        && ls.ProductCategories.Contains(cat)
                        && ls.ZipCoverage.Covers(customerZip)));

                if (!localCanCover)
                {
                    var uncoveredCats = assignedCategories
                        .Where(cat => !_suppliers.Any(ls => !ls.ZipCoverage.IsNationwide
                            && ls.ProductCategories.Contains(cat)
                            && ls.ZipCoverage.Covers(customerZip)))
                        .ToList();
                    warnings.Add($"Supplier '{s.SupplierId}' ({s.SupplierName}) selected via nationwide coverage for {string.Join(", ", uncoveredCats)} — no local supplier covers these categories in ZIP {customerZip}.");
                }
            }
        }

        // Step 5: Build response
        var routing = assignments
            .GroupBy(a => a.Supplier.SupplierId)
            .Select(g =>
            {
                var supplier = g.First().Supplier;
                return new SupplierAssignment
                {
                    SupplierId = supplier.SupplierId,
                    SupplierName = supplier.SupplierName,
                    Items = g.Select(a => new RoutedItem
                    {
                        ProductCode = a.Product.ProductCode,
                        Quantity = a.Item.Quantity,
                        Category = a.Product.Category,
                        FulfillmentMode = a.FulfillmentMode
                    }).ToList()
                };
            })
            .ToList();

        return RouteResponse.Success(order.OrderId, routing, warnings);
    }

    /// <summary>
    /// Processes multiple orders independently. Each order is routed in isolation with
    /// try/catch so one bad order can't take down the batch. Failed orders are collected
    /// into a dead-letter list and persisted to disk for ops review.
    /// </summary>
    public BatchRouteResponse RouteBatch(List<OrderRequest> orders)
    {
        var results = new List<RouteResponse>();
        var deadLetter = new List<DeadLetterEntry>();

        foreach (var order in orders)
        {
            try
            {
                var result = Route(order);
                results.Add(result);

                if (!result.Feasible)
                {
                    deadLetter.Add(new DeadLetterEntry
                    {
                        Order = order,
                        Errors = result.Errors ?? ["Unknown error"]
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error routing order {OrderId}", order.OrderId);
                var errorResponse = RouteResponse.Failure(
                    order.OrderId ?? "unknown",
                    [$"Internal error: {ex.Message}"]);
                results.Add(errorResponse);
                deadLetter.Add(new DeadLetterEntry
                {
                    Order = order,
                    Errors = [$"Internal error: {ex.Message}"]
                });
            }
        }

        string? deadLetterFile = null;
        if (deadLetter.Count > 0)
        {
            deadLetterFile = WriteDeadLetterFile(deadLetter);
        }

        return new BatchRouteResponse
        {
            Processed = results.Count(r => r.Feasible),
            Failed = results.Count(r => !r.Feasible),
            Results = results,
            DeadLetter = deadLetter.Count > 0 ? deadLetter : null,
            DeadLetterFile = deadLetterFile
        };
    }

    /// <summary>
    /// Persists unroutable orders to a JSON file for manual review and retry.
    /// Written to disk (not just logged) so ops tooling can pick them up.
    /// </summary>
    private string? WriteDeadLetterFile(List<DeadLetterEntry> deadLetter)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "dead_letter");
            Directory.CreateDirectory(dir);
            var filename = $"failed_orders_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            var path = Path.Combine(dir, filename);

            var json = System.Text.Json.JsonSerializer.Serialize(deadLetter,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);

            _logger.LogWarning("Wrote {Count} failed orders to {Path}", deadLetter.Count, path);
            return filename;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write dead letter file");
            return null;
        }
    }

    /// <summary>
    /// Validates order structure: requires at least one item, a customer ZIP,
    /// non-empty product codes, and positive quantities.
    /// </summary>
    private List<string> Validate(OrderRequest order)
    {
        var errors = new List<string>();

        if (order.Items == null || order.Items.Count == 0)
            errors.Add("Order must include at least one line item.");

        if (string.IsNullOrWhiteSpace(order.CustomerZip))
            errors.Add("Order must include a valid customer_zip.");

        if (order.Items != null)
        {
            foreach (var item in order.Items)
            {
                if (string.IsNullOrWhiteSpace(item.ProductCode))
                    errors.Add("Each item must include a product_code.");
                if (item.Quantity <= 0)
                    errors.Add($"Item '{item.ProductCode}' must have a positive quantity.");
            }
        }

        return errors;
    }

    /// <summary>
    /// Filters suppliers by category match and geographic eligibility.
    /// In non-strict mode, nationwide suppliers act as a safety net for underserved ZIPs.
    /// Mail-order expands eligibility to non-local suppliers that support shipping.
    /// </summary>
    private List<EligibleSupplier> FindEligibleSuppliers(string category, string customerZip, bool mailOrder, bool strict = false)
    {
        var eligible = new List<EligibleSupplier>();

        foreach (var supplier in _suppliers)
        {
            if (!supplier.ProductCategories.Contains(category))
                continue;

            // Strict mode only disables fuzzy product matching — nationwide suppliers
            // are valid per the spec as long as their ZIP range covers the customer.

            bool isLocal = supplier.ZipCoverage.Covers(customerZip);

            if (mailOrder)
            {
                if (isLocal)
                    eligible.Add(new(supplier, "local"));
                else if (supplier.CanMailOrder)
                    eligible.Add(new(supplier, "mail_order"));
            }
            else
            {
                if (isLocal)
                    eligible.Add(new(supplier, "local"));
            }
        }

        return eligible;
    }

    /// <summary>
    /// Greedy set-cover: each iteration picks the supplier that covers the most
    /// unassigned items (weighted by consolidation, quality, and locality).
    /// This naturally minimizes shipment count while favoring better suppliers.
    /// </summary>
    private List<Assignment> GreedyAssign(
        List<(OrderItemRequest Item, Product Product, List<EligibleSupplier> Suppliers)> items,
        string customerZip,
        bool mailOrder)
    {
        var assignments = new List<Assignment>();
        var unassigned = new HashSet<int>(Enumerable.Range(0, items.Count));

        while (unassigned.Count > 0)
        {
            // Find the supplier that covers the most unassigned items with best score
            Supplier? bestSupplier = null;
            string bestMode = "local";
            double bestScore = double.MinValue;
            var bestCoveredIndices = new List<int>();

            // Collect all candidate suppliers from unassigned items
            var candidateSuppliers = new Dictionary<string, (Supplier Supplier, string Mode)>();
            foreach (var idx in unassigned)
            {
                foreach (var es in items[idx].Suppliers)
                {
                    candidateSuppliers.TryAdd(es.Supplier.SupplierId, (es.Supplier, es.Mode));
                }
            }

            foreach (var (supplierId, (supplier, _)) in candidateSuppliers)
            {
                var coveredIndices = new List<int>();
                string mode = "local";

                foreach (var idx in unassigned)
                {
                    var match = items[idx].Suppliers.FirstOrDefault(e => e.Supplier.SupplierId == supplierId);
                    if (match != null)
                    {
                        coveredIndices.Add(idx);
                        if (match.Mode == "mail_order") mode = "mail_order";
                    }
                }

                if (coveredIndices.Count == 0) continue;

                // Score = (items covered * 10) + satisfaction + local bonus.
                // A supplier covering 2 items always beats one covering 1 item regardless
                // of quality, which is the intended consolidation-first behavior.
                double score =
                    (ConsolidationWeight * coveredIndices.Count) +
                    (QualityWeight * supplier.EffectiveScore) +
                    (mode == "local" ? LocalBonus : 0);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestSupplier = supplier;
                    bestMode = mode;
                    bestCoveredIndices = coveredIndices;
                }
            }

            if (bestSupplier == null)
                break; // Should not happen if validation passed

            foreach (var idx in bestCoveredIndices)
            {
                var actualMode = items[idx].Suppliers
                    .FirstOrDefault(e => e.Supplier.SupplierId == bestSupplier.SupplierId)?.Mode ?? bestMode;

                assignments.Add(new Assignment(bestSupplier, items[idx].Product, items[idx].Item, actualMode));
                unassigned.Remove(idx);
            }
        }

        return assignments;
    }

    private sealed record EligibleSupplier(Supplier Supplier, string Mode);
    private sealed record Assignment(Supplier Supplier, Product Product, OrderItemRequest Item, string FulfillmentMode);
}
