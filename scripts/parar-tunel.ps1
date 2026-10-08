# Encerra a publicação temporária iniciada por publicar-tunel.ps1.
# Só encerra os processos que aquele script iniciou (PIDs gravados em .publish\pids.txt).

param([switch]$Silencioso)

$root = Split-Path -Parent $PSScriptRoot
$pidFile = Join-Path $root ".publish\pids.txt"

if (Test-Path $pidFile) {
    foreach ($line in Get-Content $pidFile) {
        if ($line -match '^\d+$') {
            Stop-Process -Id ([int]$line) -Force -ErrorAction SilentlyContinue
        }
    }
    Remove-Item $pidFile
}

if (-not $Silencioso) {
    if (Test-Path "$root\.publish\url.txt") { Remove-Item "$root\.publish\url.txt" }
    Write-Host "Publicação encerrada. O link deixou de funcionar."
}
