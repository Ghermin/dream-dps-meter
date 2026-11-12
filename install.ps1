# DPS meters de Ghermin - installation en une commande :
#   irm https://raw.githubusercontent.com/Ghermin/dream-dps-meter/main/install.ps1 | iex
#
# Trouve les jeux dans les bibliothèques Steam et propose d'installer (ou mettre à jour), au choix :
#   - Dream DPS Meter      pour Shape of Dreams     -> <jeu>\Mods\DreamDpsMeter
#   - Blackveil DPS Meter  pour Raiders of Blackveil -> <jeu>\BepInEx\plugins\BlackveilDpsMeter (BepInEx 5 installé si besoin)
# Sans question : $env:DPS_METERS = 'dreams', 'blackveil' ou 'dreams,blackveil' avant la commande.
# Les mises à jour suivantes se font toutes seules au lancement du jeu.

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$repo = 'https://raw.githubusercontent.com/Ghermin/dream-dps-meter/main'
$bepinexUrl = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.6/BepInEx_win_x64_5.4.23.6.zip'
$bust = [DateTime]::UtcNow.Ticks

$meters = @(
    [pscustomobject]@{ Id = 'dreams';    Name = 'Dream DPS Meter';     Game = 'Shape of Dreams';      Exe = 'Shape of Dreams.exe' },
    [pscustomobject]@{ Id = 'blackveil'; Name = 'Blackveil DPS Meter'; Game = 'Raiders of Blackveil'; Exe = 'RoB.exe' }
)

function Get-SteamLibraries {
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
    return $roots | Select-Object -Unique
}

function Find-Game($meter, $libraries) {
    foreach ($root in $libraries) {
        # [IO.Path] et non Join-Path : Join-Path échoue sur un lecteur qui n'existe pas (E:\SteamLibrary...)
        $candidate = [IO.Path]::Combine($root, 'steamapps', 'common', $meter.Game)
        if (Test-Path -LiteralPath ([IO.Path]::Combine($candidate, $meter.Exe))) { return $candidate }
    }
    return $null
}

function Save-File($url, $out) {
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $out
}

function Install-Dreams($game) {
    $target = Join-Path $game 'Mods\DreamDpsMeter'
    New-Item -ItemType Directory -Force -Path (Join-Path $target 'about') | Out-Null
    $files = @(
        @{ src = 'release/DreamDpsMeter.dll'; dst = 'DreamDpsMeter.dll' },
        @{ src = 'about/metadata.json';       dst = 'about\metadata.json' },
        @{ src = 'about/description.txt';     dst = 'about\description.txt' },
        @{ src = 'about/icon.png';            dst = 'about\icon.png' },
        @{ src = 'about/preview.png';         dst = 'about\preview.png' }
    )
    foreach ($f in $files) { Save-File "$repo/$($f.src)?t=$bust" (Join-Path $target $f.dst) }
    $version = (Invoke-WebRequest -UseBasicParsing -Uri "$repo/release/version.txt?t=$bust").Content.Trim()
    Write-Host "  Installé : Dream DPS Meter v$version -> $target" -ForegroundColor Green
    Write-Host '  Dernière étape (une seule fois) : lance le jeu, menu "Mods" sur l''écran titre,'
    Write-Host '  active "Dream DPS Meter". Les mises à jour se feront ensuite toutes seules.'
}

function Install-Blackveil($game) {
    if (Test-Path (Join-Path $game 'BepInEx\core\BepInEx.Unity.Mono.dll')) {
        Write-Host '  BepInEx 6 est déjà installé dans ce jeu (par un autre mod).' -ForegroundColor Yellow
        Write-Host '  Blackveil DPS Meter a besoin de BepInEx 5 : retire le dossier BepInEx, winhttp.dll et' -ForegroundColor Yellow
        Write-Host '  doorstop_config.ini du dossier du jeu, puis relance cette commande.' -ForegroundColor Yellow
        return
    }
    if (-not (Test-Path (Join-Path $game 'BepInEx\core\BepInEx.dll')) -or -not (Test-Path (Join-Path $game 'winhttp.dll'))) {
        Write-Host '  Installation de BepInEx 5 (chargeur de mods, depuis github.com/BepInEx)...'
        $zip = Join-Path $env:TEMP "BepInEx_$bust.zip"
        Save-File $bepinexUrl $zip
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
        try {
            foreach ($entry in $archive.Entries) {
                if (-not $entry.Name) { continue }
                $dst = Join-Path $game ($entry.FullName -replace '/', '\')
                New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dst, $true)
            }
        } finally {
            $archive.Dispose()
            Remove-Item $zip -Force -ErrorAction SilentlyContinue
        }
    }
    $target = Join-Path $game 'BepInEx\plugins\BlackveilDpsMeter'
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    $dll = Join-Path $target 'BlackveilDpsMeter.dll'
    try {
        Save-File "$repo/blackveil/release/BlackveilDpsMeter.dll?t=$bust" $dll
    } catch {
        throw "Impossible d'écrire $dll (le jeu est peut-être ouvert : ferme-le et relance la commande)."
    }
    $version = (Invoke-WebRequest -UseBasicParsing -Uri "$repo/blackveil/release/version.txt?t=$bust").Content.Trim()
    Write-Host "  Installé : Blackveil DPS Meter v$version -> $target" -ForegroundColor Green
    Write-Host '  Rien d''autre à faire : il sera actif au prochain lancement du jeu (mises à jour automatiques).'
}

Write-Host ''
Write-Host '  DPS meters de Det''s - installation' -ForegroundColor Cyan
Write-Host ''

$libraries = Get-SteamLibraries
foreach ($m in $meters) { $m | Add-Member -NotePropertyName Path -NotePropertyValue (Find-Game $m $libraries) -Force }

# ---- choix des DPS meters
$chosen = @()
if ($env:DPS_METERS) {
    $wanted = $env:DPS_METERS.ToLower() -split '[,; ]+' | Where-Object { $_ }
    $chosen = @($meters | Where-Object { $wanted -contains $_.Id })
    if (-not $chosen) { throw "DPS_METERS='$env:DPS_METERS' : valeurs possibles 'dreams' et/ou 'blackveil'." }
} else {
    Write-Host '  Quel(s) DPS meter(s) installer ?'
    for ($i = 0; $i -lt $meters.Count; $i++) {
        $m = $meters[$i]
        $where = if ($m.Path) { "trouvé : $($m.Path)" } else { 'jeu non trouvé, chemin demandé ensuite' }
        Write-Host ("   [{0}] {1,-20} pour {2,-21} ({3})" -f ($i + 1), $m.Name, $m.Game, $where)
    }
    Write-Host ("   [{0}] Les deux" -f ($meters.Count + 1))
    $found = @($meters | Where-Object { $_.Path })
    $default = if ($found.Count -eq 1) { [string]([array]::IndexOf($meters, $found[0]) + 1) } else { [string]($meters.Count + 1) }
    $answer = Read-Host "  Ton choix (1, 2, 3 ou 1,2) [Entrée = $default]"
    if (-not $answer) { $answer = $default }
    foreach ($n in ($answer -split '[,; ]+' | Where-Object { $_ })) {
        $k = 0
        if (-not [int]::TryParse($n, [ref]$k) -or $k -lt 1 -or $k -gt $meters.Count + 1) { throw "Choix invalide : '$n'." }
        if ($k -eq $meters.Count + 1) { $chosen += $meters } else { $chosen += $meters[$k - 1] }
    }
    $seen = @{}
    $chosen = @($chosen | Where-Object { if ($seen[$_.Id]) { $false } else { $seen[$_.Id] = $true; $true } })
}

foreach ($m in $chosen) {
    Write-Host ''
    Write-Host "  == $($m.Name) ($($m.Game))" -ForegroundColor Cyan
    $game = $m.Path
    if (-not $game) {
        $game = Read-Host "  Dossier de $($m.Game) introuvable. Colle son chemin (ex: D:\SteamLibrary\steamapps\common\$($m.Game)), ou Entrée pour passer"
        if (-not $game) { Write-Host '  Ignoré.'; continue }
        if (-not (Test-Path (Join-Path $game $m.Exe))) { Write-Host "  $($m.Exe) introuvable dans '$game', ignoré." -ForegroundColor Yellow; continue }
    }
    Write-Host "  Jeu : $game"
    if ($m.Id -eq 'dreams') { Install-Dreams $game } else { Install-Blackveil $game }
}
Write-Host ''
