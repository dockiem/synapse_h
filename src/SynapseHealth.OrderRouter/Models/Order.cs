using System.Text.Json.Serialization;

namespace SynapseHealth.OrderRouter.Models;

// --- Request / Response DTOs for the routing API ---
// These classes map directly to the JSON contract. The API accepts both single
// orders and batch arrays on the same endpoint (POST /api/route).

public sealed class OrderRequest
{
    [JsonPropertyName("order_id")]
    public string? OrderId { get; set; }

    [JsonPropertyName("customer_zip")]
    public string? CustomerZip { get; set; }

    [JsonPropertyName("mail_order")]
    public bool MailOrder { get; set; }

    [JsonPropertyName("items")]
    public List<OrderItemRequest>? Items { get; set; }

    [JsonPropertyName("priority")]
    public string? Priority { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// When true, disables fuzzy product matching — product codes must match exactly.
    /// Supplier eligibility (including nationwide) is unaffected; the spec allows any
    /// supplier whose ZIP range covers the customer.
    /// </summary>
    [JsonPropertyName("strict")]
    public bool Strict { get; set; } = true;
}

public sealed class OrderItemRequest
{
    [JsonPropertyName("product_code")]
    public string? ProductCode { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}

// --- Response ---

public sealed class RouteResponse
{
    [JsonPropertyName("feasible")]
    public bool Feasible { get; set; }

    [JsonPropertyName("routing")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SupplierAssignment>? Routing { get; set; }

    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Errors { get; set; }

    [JsonPropertyName("warnings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Warnings { get; set; }

    [JsonPropertyName("order_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OrderId { get; set; }

    public static RouteResponse Failure(string orderId, List<string> errors) =>
        new() { OrderId = orderId, Feasible = false, Errors = errors };

    public static RouteResponse Failure(List<string> errors) =>
        new() { Feasible = false, Errors = errors };

    public static RouteResponse Success(string? orderId, List<SupplierAssignment> routing, List<string>? warnings = null) =>
        new()
        {
            OrderId = orderId,
            Feasible = true,
            Routing = routing,
            Warnings = warnings is { Count: > 0 } ? warnings : null
        };

    public static RouteResponse Success(List<SupplierAssignment> routing, List<string>? warnings = null) =>
        new()
        {
            Feasible = true,
            Routing = routing,
            Warnings = warnings is { Count: > 0 } ? warnings : null
        };
}

// --- Batch Response ---
// Includes a dead-letter mechanism: unroutable orders are persisted to disk
// so ops can investigate and manually fulfill them.

public sealed class BatchRouteResponse
{
    [JsonPropertyName("processed")]
    public int Processed { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("results")]
    public required List<RouteResponse> Results { get; set; }

    [JsonPropertyName("dead_letter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DeadLetterEntry>? DeadLetter { get; set; }

    [JsonPropertyName("dead_letter_file")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeadLetterFile { get; set; }
}

public sealed class DeadLetterEntry
{
    [JsonPropertyName("order")]
    public required OrderRequest Order { get; set; }

    [JsonPropertyName("errors")]
    public required List<string> Errors { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public sealed class SupplierAssignment
{
    [JsonPropertyName("supplier_id")]
    public required string SupplierId { get; set; }

    [JsonPropertyName("supplier_name")]
    public required string SupplierName { get; set; }

    [JsonPropertyName("items")]
    public required List<RoutedItem> Items { get; set; }
}

public sealed class RoutedItem
{
    [JsonPropertyName("product_code")]
    public required string ProductCode { get; set; }

    [JsonPropertyName("quantity")]
    public required int Quantity { get; set; }

    [JsonPropertyName("category")]
    public required string Category { get; set; }

    [JsonPropertyName("fulfillment_mode")]
    public required string FulfillmentMode { get; set; }
}
