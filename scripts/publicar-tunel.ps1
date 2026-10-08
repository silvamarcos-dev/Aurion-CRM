# Publica o CRM temporariamente na internet a partir deste computador (Cloudflare Quick Tunnel).
#
#  - API (.NET, Release) escuta SOMENTE em 127.0.0.1:5180 — não é exposta.
#  - Site (Next.js, produção) escuta em 127.0.0.1:3200 — é o único serviço atrás do túnel.
#  - O link https://*.trycloudflare.com muda a cada execução e funciona enquanto este PC estiver ligado.
#
# Uso:   powershell -ExecutionPolicy Bypass -File scripts\publicar-tunel.ps1 [-SemBuild]
# Parar: powershell -ExecutionPolicy Bypass -File scripts\parar-tunel.ps1

param([switch]$SemBuild)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$logs = Join-Path $root ".publish\logs"
New-Item -ItemType Directory -Force $logs | Out-Null

# ── Configuração local (nunca versionada) ──
$envFile = Join-Path $root ".env.local"
if (-not (Test-Path $envFile)) { throw "Arquivo .env.local não encontrado na raiz do projeto." }
$cfg = @{}
Get-Content $envFile | Where-Object { $_ -match '^\s*([A-Z0-9_]+)=(.*)$' } | ForEach-Object { $cfg[$Matches[1]] = $Matches[2].Trim() }
foreach ($key in "CRM_DB_CONNECTION", "CRM_JWT_SIGNING_KEY") {
    if (-not $cfg[$key]) { throw "Variável $key ausente em .env.local." }
}

& "$root\scripts\parar-tunel.ps1" -Silencioso

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:ProgramFiles\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw ".NET SDK não encontrado." }

$pg = Get-Service postgresql-x64-17 -ErrorAction SilentlyContinue
if ($pg -and $pg.Status -ne "Running") { Start-Service postgresql-x64-17 }

# ── Build ──
if (-not $SemBuild) {
    Write-Host "Compilando API (Release)..."
    & $dotnet publish "$root\backend\src\Loteadora.Crm.Api" -c Release -o "$root\.publish\api" --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar a API." }

    Write-Host "Compilando site (produção)..."
    Push-Location "$root\web"
    $env:NEXT_DIST_DIR = ".next-prod"
    & npm run build --silent
    if ($LASTEXITCODE -ne 0) { Pop-Location; throw "Falha ao compilar o site." }
    Pop-Location
}

# O servidor standalone precisa dos arquivos estáticos ao lado.
$standalone = "$root\web\.next-prod\standalone"
Copy-Item -Recurse -Force "$root\web\.next-prod\static" "$standalone\.next-prod\static"
if (Test-Path "$root\web\public") { Copy-Item -Recurse -Force "$root\web\public" "$standalone\public" }

# ── API ──
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "http://127.0.0.1:5180"
$env:ConnectionStrings__Default = $cfg["CRM_DB_CONNECTION"]
$env:Auth__SigningKey = $cfg["CRM_JWT_SIGNING_KEY"]
$env:Database__MigrateOnStartup = "true"
$pidFile = "$root\.publish\pids.txt"
$apiProc = Start-Process -PassThru -WindowStyle Hidden -FilePath $dotnet -ArgumentList "`"$root\.publish\api\Loteadora.Crm.Api.dll`"" `
    -WorkingDirectory "$root\.publish\api" -RedirectStandardOutput "$logs\api.log" -RedirectStandardError "$logs\api.err.log"
Set-Content $pidFile $apiProc.Id

# ── Site ──
$env:NODE_ENV = "production"
$env:PORT = "3200"
$env:HOSTNAME = "127.0.0.1"
$env:CRM_API_URL = "http://127.0.0.1:5180"
$webProc = Start-Process -PassThru -WindowStyle Hidden -FilePath "node" -ArgumentList "server.js" -WorkingDirectory $standalone `
    -RedirectStandardOutput "$logs\web.log" -RedirectStandardError "$logs\web.err.log"
Add-Content $pidFile $webProc.Id

Write-Host "Aguardando API e site..."
$ok = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 1
    try {
        $api = Invoke-WebRequest -UseBasicParsing -TimeoutSec 2 "http://127.0.0.1:5180/health"
        $web = Invoke-WebRequest -UseBasicParsing -TimeoutSec 5 "http://127.0.0.1:3200/login"
        if ($api.StatusCode -eq 200 -and $web.StatusCode -eq 200) { $ok = $true; break }
    } catch { }
}
if (-not $ok) { throw "API ou site não responderam. Veja os logs em $logs." }

# ── Túnel ──
$cloudflared = (Get-Command cloudflared -ErrorAction SilentlyContinue).Source
if (-not $cloudflared) { $cloudflared = "${env:ProgramFiles(x86)}\cloudflared\cloudflared.exe" }
$tunnelProc = Start-Process -PassThru -WindowStyle Hidden -FilePath $cloudflared -ArgumentList "tunnel --no-autoupdate --url http://127.0.0.1:3200" `
    -RedirectStandardOutput "$logs\tunnel.log" -RedirectStandardError "$logs\tunnel.err.log"
Add-Content $pidFile $tunnelProc.Id

$url = $null
for ($i = 0; $i -lt 60 -and -not $url; $i++) {
    Start-Sleep -Seconds 1
    $match = Select-String -Path "$logs\tunnel.err.log" -Pattern "https://[a-z0-9-]+\.trycloudflare\.com" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($match) { $url = $match.Matches[0].Value }
}
if (-not $url) { throw "O túnel não informou o endereço. Veja $logs\tunnel.err.log." }

Set-Content -Encoding utf8 "$root\.publish\url.txt" $url
Write-Host ""
Write-Host "CRM publicado em: $url" -ForegroundColor Green
Write-Host "Links de acesso gerados em Configurações > Acessos externos já usarão esse endereço."
Write-Host "Para encerrar: powershell -ExecutionPolicy Bypass -File scripts\parar-tunel.ps1"
