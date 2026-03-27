# Usage:
#   .\test-orders.ps1                      # Send all 3 sample orders as batch
#   .\test-orders.ps1 -Index 0             # Send first order only (ORD-001)
#   .\test-orders.ps1 -File my-order.json  # Send a custom order file (single or array)

param(
    [int]$Index = -1,
    [string]$File = "",
    [string]$BaseUrl = "http://localhost:8080"
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sampleFile = Join-Path $scriptDir "data\sample_orders.json"

function Send-Payload {
    param([string]$Json, [string]$Label)

    Write-Host "--- $Label ---" -ForegroundColor Cyan
    $response = Invoke-RestMethod -Uri "$BaseUrl/api/route" `
        -Method Post `
        -ContentType "application/json" `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($Json))

    $response | ConvertTo-Json -Depth 10
    Write-Host ""

    # Check for dead letter file
    if ($response.dead_letter_file) {
        Write-Host "Dead letter file written: $($response.dead_letter_file)" -ForegroundColor Yellow
    }
}

$targetFile = if ($File -ne "") { $File } else { $sampleFile }
$data = Get-Content $targetFile -Raw | ConvertFrom-Json

if ($Index -ge 0) {
    # Send single order by index
    $order = $data[$Index]
    $json = $order | ConvertTo-Json -Depth 10 -Compress
    Send-Payload -Json $json -Label "Routing $($order.order_id)"
} else {
    # Send entire file as-is (batch if array, single if object)
    $json = Get-Content $targetFile -Raw
    $label = if ($data -is [array]) { "Batch: $($data.Count) orders" } else { "Routing $($data.order_id)" }
    Send-Payload -Json $json -Label $label
}
