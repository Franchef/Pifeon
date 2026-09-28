#Requires -RunAsAdministrator

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "   STARTING DEV ENVIRONMENT SETUP (WIN)   " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$modulesDir = Join-Path $PSScriptRoot "modules"

# Verify modules directory exists
if (-not (Test-Path $modulesDir)) {
    Write-Host "[CRITICAL ERROR] Modules directory not found at: $modulesDir" -ForegroundColor Red
    exit 1
}

# Execute each module script and capture boolean return values
$results = [ordered]@{
    ".NET SDK"    = & "$modulesDir\windows_dotnet_setup.ps1"
    "Docker"      = & "$modulesDir\windows_docker_setup.ps1"
    ".NET Aspire" = & "$modulesDir\windows_aspire_setup.ps1"
}

Write-Host "`n------------------------------------------" -ForegroundColor Cyan
Write-Host "Installation Summary:" -ForegroundColor Cyan
foreach ($key in $results.Keys) {
    $status = if ($results[$key] -eq $true) { "[OK]" } else { "[FAIL]" }
    $color  = if ($results[$key] -eq $true) { "Green" } else { "Red" }
    Write-Host "$($key.PadRight(15)) : $status" -ForegroundColor $color
}
Write-Host "------------------------------------------" -ForegroundColor Cyan