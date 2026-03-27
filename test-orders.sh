#!/bin/bash
# Usage:
#   ./test-orders.sh                  # Send all 3 sample orders as batch
#   ./test-orders.sh 0                # Send first order only (ORD-001)
#   ./test-orders.sh my-order.json    # Send a custom order file (single or array)

BASE_URL="${API_URL:-http://localhost:8080}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd -W 2>/dev/null || pwd)"
SAMPLE_FILE="$SCRIPT_DIR/data/sample_orders.json"

send_payload() {
    local json="$1"
    local label="$2"
    echo "--- $label ---"
    local response
    response=$(curl -s -X POST "$BASE_URL/api/route" \
        -H "Content-Type: application/json" \
        -d "$json")

    # Pretty-print if node is available, otherwise raw
    if command -v node &>/dev/null; then
        node -e "console.log(JSON.stringify(JSON.parse(process.argv[1]), null, 2))" "$response" 2>/dev/null || echo "$response"
    else
        echo "$response"
    fi
    echo ""
}

TARGET_FILE="${1:-}"

if [ -z "$TARGET_FILE" ]; then
    # Send entire sample file as batch
    json=$(cat "$SAMPLE_FILE")
    send_payload "$json" "Batch: 3 sample orders"
elif [[ "$TARGET_FILE" =~ ^[0-9]+$ ]]; then
    # Extract single order by index
    if command -v node &>/dev/null; then
        json=$(node -e "
            const data = JSON.parse(require('fs').readFileSync(String.raw\`$SAMPLE_FILE\`, 'utf8'));
            console.log(JSON.stringify(data[$TARGET_FILE]));
        ")
        order_id=$(node -e "console.log(JSON.parse(process.argv[1]).order_id || '?')" "$json" 2>/dev/null)
        send_payload "$json" "Routing $order_id"
    else
        echo "Node.js required for index selection. Send entire file instead: $0"
        exit 1
    fi
elif [ -f "$TARGET_FILE" ]; then
    # Send custom file as-is
    filepath="$(cd "$(dirname "$TARGET_FILE")" && pwd -W 2>/dev/null || pwd)/$(basename "$TARGET_FILE")"
    json=$(cat "$filepath")
    send_payload "$json" "Sending $TARGET_FILE"
else
    echo "Usage: $0 [index|file.json]"
    exit 1
fi
