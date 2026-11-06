# Publie une nouvelle version : .\release.ps1 1.2.0
# - met à jour le numéro de version (code + metadata.json)
# - compile en Release
# - copie la DLL dans release\ et écrit release\version.txt (lus par l'installeur et la mise à jour auto)
# - commit, tag et push sur GitHub
param([Parameter(Mandatory = $true)][string]$Version)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not ($Version -match '^\d+\.\d+\.\d+$')) { throw "Version attendue au format X.Y.Z (ex: 1.2.0)" }

$versionFile = 'src\Version.cs'
(Get-Content $versionFile -Raw) -replace 'Current = "[^"]*"', "Current = `"$Version`"" | Set-Content $versionFile -NoNewline
(Get-Content 'about\metadata.json' -Raw) -replace '"modVer":\s*"[^"]*"', "`"modVer`": `"$Version`"" | Set-Content 'about\metadata.json' -NoNewline

dotnet build -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { throw "La compilation a échoué." }

New-Item -ItemType Directory -Force -Path release | Out-Null
Copy-Item 'bin\Release\netstandard2.1\DreamDpsMeter.dll' 'release\DreamDpsMeter.dll' -Force
Set-Content 'release\version.txt' $Version -NoNewline

git add -A
git commit -m "Release v$Version"
git tag "v$Version"
git push origin main --tags
Write-Host "v$Version publiée. Les joueurs la recevront au prochain lancement du jeu." -ForegroundColor Green
