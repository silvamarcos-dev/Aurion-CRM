# Sobe o ambiente de desenvolvimento: API (.NET) em :5180 e frontend (Next.js) em :3100.
# Cada um abre em uma janela própria do PowerShell; feche a janela para parar.

$root = $PSScriptRoot

$pg = Get-Service postgresql-x64-17 -ErrorAction SilentlyContinue
if ($pg -and $pg.Status -ne 'Running') {
    Write-Host "Iniciando PostgreSQL..."
    Start-Service postgresql-x64-17
}

if (-not (Test-Path "$root\web\node_modules")) {
    Write-Host "Instalando dependências do frontend..."
    npm --prefix "$root\web" install
}

Start-Process powershell -ArgumentList '-NoExit', '-Command', "Set-Location '$root'; `$Host.UI.RawUI.WindowTitle = 'CRM API :5180'; dotnet run --project backend/src/Loteadora.Crm.Api --urls http://localhost:5180 --environment Development"
Start-Process powershell -ArgumentList '-NoExit', '-Command', "Set-Location '$root\web'; `$Host.UI.RawUI.WindowTitle = 'CRM Web :3100'; npx next dev --turbopack --port 3100"

Write-Host ""
Write-Host "Aguarde ~20 segundos e abra: http://localhost:3100"
Write-Host "Senha dos usuários de demonstração: variável CRM_SEED_PASSWORD no arquivo .env.local"
