# Copie le script de développement (mh-wilds\src) dans le jeu, pour tester sans publier.
# Dans le jeu : menu REFramework (Inser) > ScriptRunner > « Reset Scripts » recharge le script sans relancer.
param([string]$GameDir)

$ErrorActionPreference = 'Stop'
if (-not $GameDir) {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    $libs = @($steam)
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        $libs += Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' | ForEach-Object { $_.Matches[0].Groups[1].Value -replace '\\\\', '\' }
    }
    $GameDir = $libs | ForEach-Object { Join-Path $_ 'steamapps\common\MonsterHunterWilds' } |
        Where-Object { Test-Path (Join-Path $_ 'MonsterHunterWilds.exe') } | Select-Object -First 1
    if (-not $GameDir) { throw "MonsterHunterWilds introuvable : .\deploy.ps1 -GameDir 'D:\...\MonsterHunterWilds'" }
}
if (-not (Test-Path (Join-Path $GameDir 'dinput8.dll'))) { Write-Host "REFramework absent (dinput8.dll) : lance d'abord l'installeur." -ForegroundColor Yellow }

$autorun = Join-Path $GameDir 'reframework\autorun'
New-Item -ItemType Directory -Force -Path (Join-Path $autorun 'wilds_dps_meter') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'src\wilds_dps_meter.lua') $autorun -Force
Copy-Item (Join-Path $PSScriptRoot 'src\wilds_dps_meter\*') (Join-Path $autorun 'wilds_dps_meter') -Recurse -Force
Write-Host "Copié dans $autorun" -ForegroundColor Green
