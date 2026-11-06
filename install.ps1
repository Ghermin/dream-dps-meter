# Dream DPS Meter - installation en une commande :
#   irm https://raw.githubusercontent.com/Ghermin/dream-dps-meter/main/install.ps1 | iex
#
# Trouve Shape of Dreams dans les bibliothèques Steam et installe (ou met à jour) le mod dans <jeu>\Mods\DreamDpsMeter.
# Les mises à jour suivantes se font toutes seules au lancement du jeu.

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$repo = 'https://raw.githubusercontent.com/Ghermin/dream-dps-meter/main'
$gameFolder = 'Shape of Dreams'

function Find-Game {
    $roots = @()
    $steam = $null
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        $props = Get-ItemProperty $key -ErrorAction SilentlyContinue
        if ($props) {
            if ($props.SteamPath) { $steam = $props.SteamPath; break }
            if ($props.InstallPath) { $steam = $props.InstallPath; break }
        }
    }
    if ($steam) {
        $steam = $steam -replace '/', '\'
        $roots += $steam
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $roots += ($m.Groups[1].Value -replace '\\\\', '\')
            }
        }
    }
    $roots += 'C:\Program Files (x86)\Steam', 'C:\Program Files\Steam', 'D:\SteamLibrary', 'E:\SteamLibrary', 'D:\Steam', 'E:\Steam'
    foreach ($root in ($roots | Select-Object -Unique)) {
        $candidate = Join-Path $root "steamapps\common\$gameFolder"
        if (Test-Path (Join-Path $candidate 'Shape of Dreams.exe')) { return $candidate }
    }
    return $null
}

Write-Host ''
Write-Host '  Dream DPS Meter - installation' -ForegroundColor Cyan
Write-Host ''

$game = Find-Game
if (-not $game) {
    $game = Read-Host "  Dossier de Shape of Dreams introuvable. Colle son chemin (ex: D:\SteamLibrary\steamapps\common\Shape of Dreams)"
    if (-not (Test-Path (Join-Path $game 'Shape of Dreams.exe'))) { throw "Shape of Dreams.exe introuvable dans '$game'." }
}
Write-Host "  Jeu trouvé : $game"

$target = Join-Path $game 'Mods\DreamDpsMeter'
New-Item -ItemType Directory -Force -Path (Join-Path $target 'about') | Out-Null

$files = @(
    @{ src = 'release/DreamDpsMeter.dll'; dst = 'DreamDpsMeter.dll' },
    @{ src = 'about/metadata.json';       dst = 'about\metadata.json' },
    @{ src = 'about/description.txt';     dst = 'about\description.txt' },
    @{ src = 'about/icon.png';            dst = 'about\icon.png' },
    @{ src = 'about/preview.png';         dst = 'about\preview.png' }
)
$bust = [DateTime]::UtcNow.Ticks
foreach ($f in $files) {
    $out = Join-Path $target $f.dst
    Invoke-WebRequest -UseBasicParsing -Uri "$repo/$($f.src)?t=$bust" -OutFile $out
}
$version = (Invoke-WebRequest -UseBasicParsing -Uri "$repo/release/version.txt?t=$bust").Content.Trim()

Write-Host "  Installé : Dream DPS Meter v$version -> $target" -ForegroundColor Green
Write-Host ''
Write-Host '  Dernière étape (une seule fois) : lance le jeu, menu "Mods" sur l''écran titre,'
Write-Host '  active "Dream DPS Meter". Les mises à jour se feront ensuite toutes seules.'
Write-Host ''
