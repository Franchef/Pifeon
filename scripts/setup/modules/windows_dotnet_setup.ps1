function Test-DotnetInstalled {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        return $false
    }

    return [bool](dotnet --list-sdks | Where-Object { $_ -match '^11\.' })
}

if (Test-DotnetInstalled) {
    Write-Host "[OK] .NET 11 SDK is already installed." -ForegroundColor Green
    return $true
}

Write-Host "[INFO] .NET 11 SDK not found. Installing via Winget..." -ForegroundColor Yellow
winget install --id Microsoft.DotNet.SDK.11 --silent --accept-package-agreements --accept-source-agreements

# Refresh PATH for the current session
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

if (Test-DotnetInstalled) {
    Write-Host "[SUCCESS] .NET SDK installed successfully." -ForegroundColor Green
    return $true
} else {
    Write-Host "[ERROR] Failed to install .NET SDK." -ForegroundColor Red
    return $false
}