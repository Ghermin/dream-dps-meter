# Publie une nouvelle version d'un des DPS meters :
#   .\release.ps1                      -> Dream DPS Meter (Shape of Dreams), dernier chiffre +1 (1.1.0 -> 1.1.1)
#   .\release.ps1 1.2.0                -> Dream DPS Meter, version choisie
#   .\release.ps1 -Mod blackveil       -> Blackveil DPS Meter (Raiders of Blackveil), dernier chiffre +1
#   .\release.ps1 -Mod blackveil 0.2.0 -> Blackveil DPS Meter, version choisie
# - met à jour le numéro de version (code, et pour Shape of Dreams metadata.json + description du mod)
# - compile en Release (trouve le jeu tout seul)
# - copie la DLL dans release\ (ou blackveil\release\) et écrit version.txt (lus par l'installeur et la mise à jour auto)
# - commit, tag et push sur GitHub : les joueurs la reçoivent au prochain lancement du jeu
param(
    [Parameter(Position = 0)][string]$Version,
    [ValidateSet('dreams', 'blackveil')][string]$Mod = 'dreams'
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if ($Mod -eq 'dreams') {
    $cfg = @{
        Name = 'Dream DPS Meter'; Dir = '.'; Project = 'DreamDpsMeter.csproj'; Dll = 'DreamDpsMeter.dll'
        Game = 'Shape of Dreams'; Probe = 'Shape of Dreams_Data\Managed\Assembly-CSharp.dll'; TagPrefix = 'v'
    }
} else {
    $cfg = @{
        Name = 'Blackveil DPS Meter'; Dir = 'blackveil'; Project = 'blackveil\BlackveilDpsMeter.csproj'; Dll = 'BlackveilDpsMeter.dll'
        Game = 'Raiders of Blackveil'; Probe = 'BepInEx\core\BepInEx.dll'; TagPrefix = 'blackveil-v'
    }
}
$releaseDir = Join-Path $cfg.Dir 'release'
$versionTxt = Join-Path $releaseDir 'version.txt'

# ---- numéro de version
$current = if (Test-Path $versionTxt) { (Get-Content $versionTxt -Raw).Trim() } else { '0.0.0' }
if (-not $Version) {
    $parts = $current.Split('.')
    $Version = "$($parts[0]).$($parts[1]).$([int]$parts[2] + 1)"
}
if (-not ($Version -match '^\d+\.\d+\.\d+$')) { throw "Version attendue au format X.Y.Z (ex: 1.2.0)" }
if ([version]$Version -le [version]$current) { throw "La version $Version doit être supérieure à la version publiée ($current)." }
$tag = "$($cfg.TagPrefix)$Version"
if (git tag --list $tag) { throw "Le tag $tag existe déjà." }

# ---- dossier du jeu (pour les DLL référencées)
function Find-GameDir {
    $candidates = @()
    if ($Mod -eq 'dreams') { $candidates += (Join-Path $PSScriptRoot '..\..') }
    $steam = @(
        (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath,
        "${env:ProgramFiles(x86)}\Steam",
        "$env:ProgramFiles\Steam"
    ) | Where-Object { $_ }
    foreach ($s in $steam) {
        $vdf = Join-Path $s 'steamapps\libraryfolders.vdf'
        $libs = @($s)
        if (Test-Path $vdf) {
            $libs += Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' |
                ForEach-Object { $_.Matches[0].Groups[1].Value -replace '\\\\', '\' }
        }
        foreach ($lib in $libs) { $candidates += Join-Path $lib "steamapps\common\$($cfg.Game)" }
    }
    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c $cfg.Probe)) { return (Resolve-Path $c).Path }
    }
    throw "$($cfg.Game) introuvable (ou BepInEx absent pour Raiders of Blackveil : lance d'abord install.ps1)."
}
$gameDir = Find-GameDir
Write-Host "Jeu : $gameDir"
Write-Host "$($cfg.Name) : $current -> $Version" -ForegroundColor Cyan

$versionFile = Join-Path $cfg.Dir 'src\Version.cs'
(Get-Content $versionFile -Raw) -replace 'Current = "[^"]*"', "Current = `"$Version`"" | Set-Content $versionFile -NoNewline
if ($Mod -eq 'dreams') {
    (Get-Content 'about\metadata.json' -Raw) -replace '"modVer":\s*"[^"]*"', "`"modVer`": `"$Version`"" | Set-Content 'about\metadata.json' -NoNewline
    (Get-Content 'about\description.txt' -Raw) -replace '(?m)^Version \d+\.\d+\.\d+', "Version $Version" | Set-Content 'about\description.txt' -NoNewline
}

dotnet build $cfg.Project -c Release "-p:GameDir=$gameDir" "-p:DeployToGame=false" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "La compilation a échoué." }

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
Copy-Item (Join-Path $cfg.Dir "bin\Release\netstandard2.1\$($cfg.Dll)") (Join-Path $releaseDir $cfg.Dll) -Force
Set-Content $versionTxt $Version -NoNewline

git add -A
git commit -m "Release $($cfg.Name) v$Version"
git tag $tag
git push origin main --tags
if ($LASTEXITCODE -ne 0) { throw "Le push a échoué." }
Write-Host "$($cfg.Name) v$Version publiée. Les joueurs la recevront au prochain lancement du jeu." -ForegroundColor Green
