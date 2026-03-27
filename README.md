# Multi-Supplier Order Router

A .NET 10 service that routes multi-item medical supply orders to optimal suppliers based on product capabilities, geographic coverage, consolidation, and quality scores.

## Quick Start

Only Docker is required. No local SDK installation needed.

```bash
# 1. Start the service
docker compose up --build

# 2. Open the browser
http://localhost:8080
```

That's it. The upload UI lets you drag and drop an order JSON file, see results, and download output.

### Run Tests

```bash
docker compose --profile test run --rm test
```

## API

### `POST /api/route`

Route an order to suppliers. Always returns HTTP 200.

**Request:**
```json
{
  "order_id": "ORD-001",
  "customer_zip": "10015",
  "mail_order": false,
  "items": [
    { "product_code": "WC-STD-001", "quantity": 1 },
    { "product_code": "OX-PORT-024", "quantity": 1 }
  ]
}
```

**Success response:**
```json
{
  "feasible": true,
  "routing": [
    {
      "supplier_id": "SUP-005",
      "supplier_name": "Respiratory Care Co Co",
      "items": [
        {
          "product_code": "WC-STD-001",
          "quantity": 1,
          "category": "wheelchair",
          "fulfillment_mode": "local"
        }
      ]
    }
  ]
}
```

**Failure response:**
```json
{
  "feasible": false,
  "errors": ["No supplier available for 'WC-STD-001' (category: wheelchair) in ZIP 90210"]
}
```

### `GET /health`

Health check endpoint.

## Test with sample orders

The endpoint accepts **both single orders and arrays**. Send an array and it processes all orders in one request, returning successes and failures together. Failed orders are written to a `dead_letter/` file on the server.

You have multiple options to test:

| Method | How |
|--------|-----|
| **Browser UI** | Open `http://localhost:8080` — drag & drop a JSON file, see progress bar, download results |
| **Postman** | POST to `http://localhost:8080/api/route` with JSON body (single order or array) |
| **curl** | `curl -X POST http://localhost:8080/api/route -d @data/sample_orders.json -H "Content-Type: application/json"` |
| **PowerShell** | `.\test-orders.ps1` (auto-loads `data/sample_orders.json`) |
| **Bash** | `./test-orders.sh` (auto-loads `data/sample_orders.json`) |

The `strict` field defaults to `true` (exact product code matching). Set `"strict": false` to enable resilient mode with fuzzy matching for typos.

### PowerShell (Windows)
```powershell
# Send all 3 sample orders as a batch
.\test-orders.ps1

# Send a specific order by index (0, 1, or 2)
.\test-orders.ps1 -Index 0    # ORD-001: NYC, wheelchair + oxygen
.\test-orders.ps1 -Index 1    # ORD-002: Houston, 4-item rush
.\test-orders.ps1 -Index 2    # ORD-003: Boston, mail-order respiratory

# Send a custom order file (single object or array)
.\test-orders.ps1 -File my-order.json

# Override port if needed
.\test-orders.ps1 -BaseUrl http://localhost:9090
```

### Bash (Linux/macOS/WSL)
```bash
./test-orders.sh              # Send all 3 as batch
./test-orders.sh 0            # Send specific order by index
./test-orders.sh my-order.json # Send custom file
API_URL=http://localhost:9090 ./test-orders.sh  # Override port if needed
```

### curl
```bash
# Send the sample file directly as a batch
curl -X POST http://localhost:8080/api/route \
  -H "Content-Type: application/json" \
  -d @data/sample_orders.json
```

## Project Structure

```
src/SynapseHealth.OrderRouter/
  Program.cs              - Minimal API setup
  Models/                 - Request/response DTOs, domain models
  Data/DataLoader.cs      - CSV parsing with RFC 4180 support
  Services/               - Routing algorithm, fuzzy matching
  Utils/ZipCoverage.cs    - ZIP code parsing and normalization

tests/SynapseHealth.OrderRouter.Tests/
  ZipCoverageTests.cs          - ZIP parsing edge cases
  ProductMatcherTests.cs       - Fuzzy matching
  OrderRouterServiceTests.cs   - Routing algorithm
  ApiIntegrationTests.cs       - HTTP endpoint tests
  SampleOrderTests.cs          - ORD-001, ORD-002, ORD-003 regression
```
