# DPS meters - installation en une commande (même chose que l'installeur, sans fenêtre) :
#   irm https://raw.githubusercontent.com/Ghermin/dream-dps-meter/main/install.ps1 | iex
#
# Lit games.json (la liste des DPS meters), trouve les jeux dans les bibliothèques Steam et propose
# d'installer (ou mettre à jour) ceux qu'on veut.
# Sans question : $env:DPS_METERS = 'dreams', 'blackveil' ou 'dreams,blackveil' avant la commande.
# Les mises à jour suivantes se font toutes seules au lancement du jeu.

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$repo = 'https://raw.githubusercontent.com/Ghermin/dream-dps-meter/main'
$bust = [DateTime]::UtcNow.Ticks

$manifest = Invoke-RestMethod -UseBasicParsing -Uri "$repo/games.json?t=$bust"
$meters = @($manifest.meters)

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
        $candidate = [IO.Path]::Combine($root, 'steamapps', 'common', $meter.game)
        if (Test-Path -LiteralPath ([IO.Path]::Combine($candidate, $meter.exe))) { return $candidate }
    }
    return $null
}

function Save-File($url, $out) {
    New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $out
}

function Install-Loader($id, $game) {
    $loader = $manifest.loaders.$id
    if (-not $loader) { return $true }
    if ($loader.conflict -and (Test-Path (Join-Path $game $loader.conflict))) {
        Write-Host "  $($loader.conflictMessage)" -ForegroundColor Yellow
        return $false
    }
    $missing = @($loader.present | Where-Object { -not (Test-Path (Join-Path $game $_)) })
    if (-not $missing) { return $true }
    Write-Host "  Installation de $($loader.name) (chargeur de mods)..."
    $zip = Join-Path $env:TEMP "dps-loader-$bust.zip"
    Save-File $loader.url $zip
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
    return $true
}

function Install-Meter($m, $game) {
    if (Get-Process -Name $m.process -ErrorAction SilentlyContinue) {
        Write-Host "  $($m.game) est ouvert : ferme le jeu puis relance l'installation." -ForegroundColor Yellow
        return
    }
    if (-not (Install-Loader $m.loader $game)) { return }
    $target = Join-Path $game $m.target
    foreach ($f in $m.files) { Save-File "$repo/$($f.src)?t=$bust" (Join-Path $target $f.dst) }
    $version = (Invoke-WebRequest -UseBasicParsing -Uri "$repo/$($m.version)?t=$bust").Content.Trim()
    Write-Host "  Installé : $($m.name) v$version" -ForegroundColor Green
    Write-Host "  $($m.after)"
}

Write-Host ''
Write-Host '  DPS meters - installation' -ForegroundColor Cyan
Write-Host ''

$libraries = Get-SteamLibraries
foreach ($m in $meters) { $m | Add-Member -NotePropertyName Path -NotePropertyValue (Find-Game $m $libraries) -Force }

# ---- choix des DPS meters
$chosen = @()
if ($env:DPS_METERS) {
    $wanted = $env:DPS_METERS.ToLower() -split '[,; ]+' | Where-Object { $_ }
    $chosen = @($meters | Where-Object { $wanted -contains $_.id })
    if (-not $chosen) { throw "DPS_METERS='$env:DPS_METERS' : valeurs possibles $(($meters | ForEach-Object { "'$($_.id)'" }) -join ', ')." }
} else {
    Write-Host '  Quel(s) DPS meter(s) installer ?'
    for ($i = 0; $i -lt $meters.Count; $i++) {
        $m = $meters[$i]
        $where = if ($m.Path) { 'jeu trouvé' } else { 'jeu non trouvé, chemin demandé ensuite' }
        Write-Host ("   [{0}] {1,-20} pour {2,-21} ({3})" -f ($i + 1), $m.name, $m.game, $where)
    }
    $all = $meters.Count + 1
    Write-Host ("   [{0}] Tous" -f $all)
    $found = @($meters | Where-Object { $_.Path })
    $default = if ($found.Count -eq 1) { [string]([array]::IndexOf($meters, $found[0]) + 1) } else { [string]$all }
    $answer = Read-Host "  Ton choix (ex : 1, ou 1,2) [Entrée = $default]"
    if (-not $answer) { $answer = $default }
    foreach ($n in ($answer -split '[,; ]+' | Where-Object { $_ })) {
        $k = 0
        if (-not [int]::TryParse($n, [ref]$k) -or $k -lt 1 -or $k -gt $all) { throw "Choix invalide : '$n'." }
        if ($k -eq $all) { $chosen += $meters } else { $chosen += $meters[$k - 1] }
    }
    $seen = @{}
    $chosen = @($chosen | Where-Object { if ($seen[$_.id]) { $false } else { $seen[$_.id] = $true; $true } })
}

foreach ($m in $chosen) {
    Write-Host ''
    Write-Host "  == $($m.name) ($($m.game))" -ForegroundColor Cyan
    $game = $m.Path
    if (-not $game) {
        $game = Read-Host "  Dossier de $($m.game) introuvable. Colle son chemin (ex : D:\SteamLibrary\steamapps\common\$($m.game)), ou Entrée pour passer"
        if (-not $game) { Write-Host '  Ignoré.'; continue }
        if (-not (Test-Path (Join-Path $game $m.exe))) { Write-Host "  $($m.exe) introuvable dans ce dossier, ignoré." -ForegroundColor Yellow; continue }
    }
    try {
        Install-Meter $m $game
    } catch {
        Write-Host "  Échec : $($_.Exception.Message)" -ForegroundColor Red
    }
}
Write-Host ''
