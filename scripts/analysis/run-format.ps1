# scripts/analysis/run-format.ps1
Param(
    [switch]$CheckOnly # Se presente, verifica solo senza modificare i file (ottimo per la CI/CD)
)

Write-Host "🎨 Avvio controllo formattazione del codice basato su .editorconfig..." -ForegroundColor Cyan

if ($CheckOnly) {
    # Verifica che il codice rispetti l'.editorconfig. Se fallisce, restituisce un errore (Exit Code != 0)
    dotnet format whitespace --verify-no-changes
    dotnet format style --verify-no-changes
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "❌ Il codice non è formattato correttamente secondo l'.editorconfig!"
        Exit 1
    }
    Write-Host "✅ Tutto il codice è formattato correttamente!" -ForegroundColor Green
} else {
    # Corregge automaticamente gli spazi, i tab e lo stile del codice C# in tutta la soluzione
    Write-Host "🛠️ Applicazione delle correzioni automatiche..." -ForegroundColor Yellow
    dotnet format whitespace
    dotnet format style
    Write-Host "✅ Formattazione completata con successo!" -ForegroundColor Green
}
