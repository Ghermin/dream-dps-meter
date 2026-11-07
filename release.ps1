# Publie une nouvelle version :
#   .\release.ps1          -> incrémente le dernier chiffre (1.1.0 -> 1.1.1)
#   .\release.ps1 1.2.0    -> version choisie
# - met à jour le numéro de version (code + metadata.json)
# - compile en Release (trouve le jeu tout seul, que le dépôt soit dans Mods\ ou ailleurs)
# - copie la DLL dans release\ et écrit release\version.txt (lus par l'installeur et la mise à jour auto)
# - commit, tag et push sur GitHub : les joueurs la reçoivent au prochain lancement du jeu
param([string]$Version)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# ---- numéro de version
$current = (Get-Content 'release\version.txt' -Raw).Trim()
if (-not $Version) {
    $parts = $current.Split('.')
    $Version = "$($parts[0]).$($parts[1]).$([int]$parts[2] + 1)"
}
if (-not ($Version -match '^\d+\.\d+\.\d+$')) { throw "Version attendue au format X.Y.Z (ex: 1.2.0)" }
if ([version]$Version -le [version]$current) { throw "La version $Version doit être supérieure à la version publiée ($current)." }
if (git tag --list "v$Version") { throw "Le tag v$Version existe déjà." }

# ---- dossier du jeu (pour les DLL référencées)
function Find-GameDir {
    $candidates = @((Join-Path $PSScriptRoot '..\..'))
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
        foreach ($lib in $libs) { $candidates += Join-Path $lib 'steamapps\common\Shape of Dreams' }
    }
    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c 'Shape of Dreams_Data\Managed\Assembly-CSharp.dll')) { return (Resolve-Path $c).Path }
    }
    throw "Shape of Dreams introuvable (ni dans ..\.., ni dans les bibliothèques Steam)."
}
$gameDir = Find-GameDir
Write-Host "Jeu : $gameDir"
Write-Host "Version : $current -> $Version" -ForegroundColor Cyan

$versionFile = 'src\Version.cs'
(Get-Content $versionFile -Raw) -replace 'Current = "[^"]*"', "Current = `"$Version`"" | Set-Content $versionFile -NoNewline
(Get-Content 'about\metadata.json' -Raw) -replace '"modVer":\s*"[^"]*"', "`"modVer`": `"$Version`"" | Set-Content 'about\metadata.json' -NoNewline

dotnet build -c Release "-p:GameDir=$gameDir" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "La compilation a échoué." }

New-Item -ItemType Directory -Force -Path release | Out-Null
Copy-Item 'bin\Release\netstandard2.1\DreamDpsMeter.dll' 'release\DreamDpsMeter.dll' -Force
Set-Content 'release\version.txt' $Version -NoNewline

git add -A
git commit -m "Release v$Version"
git tag "v$Version"
git push origin main --tags
if ($LASTEXITCODE -ne 0) { throw "Le push a échoué." }
Write-Host "v$Version publiée. Les joueurs la recevront au prochain lancement du jeu." -ForegroundColor Green
