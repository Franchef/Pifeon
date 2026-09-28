function Test-DockerInstalled {
    return [bool](Get-Command docker -ErrorAction SilentlyContinue)
}

if (Test-DockerInstalled) {
    Write-Host "[OK] Docker Desktop is already installed." -ForegroundColor Green
    return $true
}

Write-Host "[INFO] Docker Desktop not found. Installing via Winget..." -ForegroundColor Yellow
winget install --id Docker.DockerDesktop --silent --accept-package-agreements --accept-source-agreements

# Refresh PATH for the current session
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

if (Test-DockerInstalled) {
    Write-Host "[SUCCESS] Docker Desktop installed successfully." -ForegroundColor Green
    return $true
} else {
    Write-Host "[WARNING] Docker Desktop installation completed, but a system reboot may be required." -ForegroundColor Yellow
    return $false
}