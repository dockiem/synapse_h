# Technical Write-Up: Multi-Supplier Order Router

## Architecture Overview

This service is built with **.NET 10** using **ASP.NET Core Minimal API**, targeting a Linux Docker runtime. The architecture separates concerns into three layers:

1. **Data Layer** — loads and normalizes messy CSV/JSON data at startup
2. **Service Layer** — routing algorithm and fuzzy matching
3. **API Layer** — thin HTTP endpoints that delegate to services via DI

## Data Quality Challenges

The supplier and product data contains intentional real-world messiness. Every issue was discovered through data exploration and handled with explicit, tested logic.

### ZIP Code Formats (5 distinct patterns)

The `service_zips` column contains:

| Format | Example | Handling |
|--------|---------|----------|
| Explicit list | `"10001, 10002, 10003"` | Split on comma, add to `HashSet<string>` |
| Single range | `"10255-10275"` | Parse start/end, range membership check |
| Multiple ranges | `"2164-2213, 2143-2193"` | Split, detect range vs explicit per token |
| Nationwide | `"00100-99999"` | `IsNationwide = true`, matches everything |
| Mixed | `"77176-77209, 77216, 77075"` | Handles both ranges and explicit in same field |

**Critical edge case — Boston leading zeros**: Massachusetts ZIP codes are stored as `2130` instead of `02130` (leading zero stripped during data import). The solution pads all ZIP strings to 5 digits with `PadLeft(5, '0')`, applied to both supplier data and incoming order ZIPs.

**Trailing quotes**: Some ZIP values contain stray `"` characters (e.g., `10046"`), stripped before parsing.

**Design decision**: Ranges are NOT expanded into sets. The nationwide range alone would produce ~100K entries. Instead, `ZipCoverage` stores ranges as `(int Start, int End)` tuples with O(R) membership checking where R is the number of ranges (typically 1-3).

### Column Name Typo

The CSV header reads `suplier_name` (missing 'p'). The data loader handles both `suplier_name` and `supplier_name` column names.

### Category Case Mismatch

Both `products.csv` and `suppliers.csv` contain `CPAP` and `cpap`, `CPM machine` and `cpm machine`. All categories are normalized to lowercase at load time via `ToLowerInvariant()`.

### Satisfaction Scores

Mixed types: integers (`7`), decimals (`8.4`), and the string `"no ratings yet"`. Parsed as `double?` with `null` for unrated suppliers. The routing algorithm uses a default score of `5.0` (neutral midpoint on the 1-10 scale) for unrated suppliers.

### Duplicate Product Codes

5 product codes appear twice in `products.csv`. The loader keeps the first occurrence and logs a warning for each duplicate.

## Routing Algorithm

The core problem is a **weighted set cover** — assign items to suppliers minimizing shipments while maximizing quality. The algorithm uses a **greedy approach** with composite scoring.

### Priority Order (as specified)

1. **Feasibility** — enforced as a hard filter (ineligible suppliers are excluded)
2. **Consolidation** — weighted 10x in the scoring function
3. **Quality** — weighted 1x (satisfaction score)
4. **Geographic preference** — 0.5 bonus for local over mail-order

### Scoring Formula

```
score = (10 * categoriesCovered) + (1 * satisfactionScore) + (0.5 if local)
```

This means a generalist covering 2 categories (score: 20 + quality + local) always beats a specialist covering 1 (score: 10 + quality + local), which correctly prioritizes consolidation.

### Algorithm Steps

1. **Validate** — check for empty items, missing ZIP, invalid product codes
2. **Resolve** — map product codes to categories (with fuzzy matching for typos)
3. **Filter** — for each category, find eligible suppliers based on ZIP coverage and mail-order rules
4. **Assign** — greedy set cover: pick the highest-scoring supplier, assign its coverable items, repeat until all items are assigned
5. **Build response** — group assignments by supplier

### Why Greedy Over Exact?

Set cover is NP-hard in general, but with order sizes of 2-4 items and pre-filtered candidate pools, even brute-force would work. The greedy approach was chosen because:
- It's cleaner to implement and explain
- It produces optimal or near-optimal results for small item counts
- It scales well if order sizes grow

## Fuzzy Matching for Typos

Since the intake team processes orders that may contain typos, the `ProductMatcher` implements a three-tier lookup:

1. **Exact match** — O(1) dictionary lookup
2. **Case-insensitive match** — dictionary uses `OrdinalIgnoreCase` comparer
3. **Levenshtein distance** — allows up to 25% of the longer string's length as edits. Corrections are surfaced in a `warnings` array on the response so callers know what was auto-corrected.

## Testing Strategy

**36 tests** across 5 test classes:

- **ZipCoverageTests** (10 tests) — every ZIP format variant, leading zeros, trailing quotes, empty input
- **ProductMatcherTests** (8 tests) — exact, case-insensitive, fuzzy, no-match, Levenshtein correctness
- **OrderRouterServiceTests** (8 tests) — consolidation vs specialists, quality preference, local vs mail-order, unrated suppliers, validation errors
- **ApiIntegrationTests** (4 tests) — HTTP endpoint behavior via `WebApplicationFactory`
- **SampleOrderTests** (3 tests) — end-to-end regression against the 3 provided orders, including ORD-003 which validates the Boston ZIP leading-zero fix

## Technology Choices

| Choice | Rationale |
|--------|-----------|
| .NET 10 | Latest runtime, excellent performance, native async/await, strong typing |
| Minimal API | Lightweight, no ceremony, perfect for a focused microservice |
| Linux Docker | Production-standard, smaller image, cross-platform |
| xUnit | Standard .NET testing framework |
| WebApplicationFactory | In-process integration testing without network overhead |
| No external CSV library | Hand-rolled RFC 4180 parser to handle the specific data quality issues cleanly |
| No ORM | Data is read-only CSV files loaded at startup - a dictionary is the right abstraction |

## Production Evolution - Queue-Based Architecture

The current implementation processes orders synchronously in a single API. This works well for the exercise scope (3 sample orders, 102 test orders), but would not scale for production workloads with hundreds of thousands of orders or file sizes in the hundreds of megabytes.

### Current Limitations

- **Memory**: entire JSON body is read into memory and deserialized at once
- **Timeout**: large batches block the HTTP request until all orders are processed
- **No horizontal scaling**: a single process handles all routing sequentially
- **Custom dead letter**: we implemented our own dead letter file persistence

### Proposed Architecture

```
Client uploads file
       |
       v
  API Gateway (accepts file, returns job ID immediately)
       |
       v
  Splits orders into individual queue messages
       |
       v
  Azure Service Bus / Queue Storage
       |
       v
  Azure Function App (queue trigger)
    - Picks up one order at a time
    - Routes using the same OrderRouterService logic
    - Writes result to Cosmos DB / Blob Storage
    - Auto-retries on transient failure
    - Poison messages go to built-in dead letter queue
       |
       v
  Results Store
       |
       v
  Client polls GET /api/jobs/{id} or receives webhook notification
```

### Why This Architecture

| Concern | Current | Queue-Based |
|---------|---------|-------------|
| Scalability | Single process, sequential | Function App scales out to N concurrent workers |
| Fault isolation | try/catch per order in memory | Each order is an independent invocation |
| Retry | None (dead letter file only) | Built-in queue retry with backoff |
| Dead letter | Custom JSON file on disk | Native queue dead letter with message metadata |
| Timeout | Client waits for entire batch | Client gets job ID immediately, polls for status |
| Observability | Application logs | Per-invocation metrics, distributed tracing |
| Cost | Always running | Pay-per-execution, scales to zero |

### Migration Path

The routing logic (`OrderRouterService`, `ProductMatcher`, `ZipCoverage`, `DataLoader`) is already cleanly separated from the HTTP layer. Moving to a Function App would require:

1. New Function App project referencing the existing service library
2. Queue trigger function that deserializes one `OrderRequest` and calls `IOrderRouter.Route()`
3. Output binding to write results to a store
4. A thin API endpoint to accept uploads, split into messages, and return a job ID
5. A status endpoint to poll for results

The core routing code would not change at all - only the hosting and I/O layer.
