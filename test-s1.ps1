# Test S1 Scenario Start
Write-Host "Testing S1 scenario..."

# Step 1: Get the page and extract anti-forgery token
$response = Invoke-WebRequest -Uri "http://localhost:5100" -SessionVariable session

# Extract token from hidden input
$pattern = '<input name="__RequestVerificationToken" type="hidden" value="([^"]+)"'
if ($response.Content -match $pattern) {
    $token = $matches[1]
    $tokenPreview = $token.Substring(0, [Math]::Min(20, $token.Length))
    Write-Host "Anti-forgery token extracted: $tokenPreview..."
} else {
    Write-Host "Failed to extract anti-forgery token"
    exit 1
}

# Step 2: POST to StartScenario handler
try {
    $body = @{
        scenarioId = "S1"
    } | ConvertTo-Json

    $headers = @{
        "Content-Type" = "application/json"
        "RequestVerificationToken" = $token
    }

    Write-Host "Sending POST request to start S1..."
    $startResponse = Invoke-WebRequest `
        -Uri "http://localhost:5100/Index?handler=StartScenario" `
        -Method POST `
        -Body $body `
        -Headers $headers `
        -WebSession $session `
        -UseBasicParsing

    Write-Host "HTTP Status: $($startResponse.StatusCode)"
    Write-Host "Content-Type: $($startResponse.Headers['Content-Type'])"
    Write-Host "Response Body:"
    Write-Host $startResponse.Content
    
    # Parse JSON
    $result = $startResponse.Content | ConvertFrom-Json
    if ($result.success) {
        Write-Host "S1 started successfully"
        Write-Host "CycleId: $($result.cycleId)"
        Write-Host "DealerUrn: $($result.dealerUrn)"
    } else {
        Write-Host "S1 failed: $($result.error)"
    }
} catch {
    Write-Host "Request failed: $($_.Exception.Message)"
    if ($_.Exception.Response) {
        $statusCode = [int]$_.Exception.Response.StatusCode
        Write-Host "HTTP Status: $statusCode"
        
        try {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $responseBody = $reader.ReadToEnd()
            Write-Host "Response Body: $responseBody"
        } catch {
            Write-Host "Could not read response body"
        }
    }
    exit 1
}
