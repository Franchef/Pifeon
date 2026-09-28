function Test-DotnetInstalled {
    return [bool](Get-Command dotnet -ErrorAction SilentlyContinue)
}

if (Test-DotnetInstalled) {
    Write-Host "[OK] .NET SDK is already installed." -ForegroundColor Green
    return $true
}

Write-Host "[INFO] .NET SDK not found. Installing via Winget..." -ForegroundColor Yellow
winget install --id Microsoft.DotNet.SDK.8 --silent --accept-package-agreements --accept-source-agreements

# Refresh PATH for the current session
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

if (Test-DotnetInstalled) {
    Write-Host "[SUCCESS] .NET SDK installed successfully." -ForegroundColor Green
    return $true
} else {
    Write-Host "[ERROR] Failed to install .NET SDK." -ForegroundColor Red
    return $false
}