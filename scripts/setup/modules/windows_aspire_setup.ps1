function Test-AspireInstalled {
    $workloads = dotnet workload list 2>$null
    return [bool]($workloads -match "aspire")
}

if (Test-AspireInstalled) {
    Write-Host "[OK] .NET Aspire Workload is already installed." -ForegroundColor Green
    return $true
}

# Ensure .NET SDK exists before attempting to install workload
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "[ERROR] Cannot install .NET Aspire: .NET SDK is missing." -ForegroundColor Red
    return $false
}

Write-Host "[INFO] Installing .NET Aspire Workload..." -ForegroundColor Yellow
dotnet workload install aspire

if (Test-AspireInstalled) {
    Write-Host "[SUCCESS] .NET Aspire Workload installed successfully." -ForegroundColor Green
    return $true
} else {
    Write-Host "[ERROR] Failed to install .NET Aspire Workload." -ForegroundColor Red
    return $false
}