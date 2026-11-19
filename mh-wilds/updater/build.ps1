# Compile le plugin de mise à jour (wilds_dps_meter_updater.dll) avec Zig (zig cc, cible MinGW, sans dépendance).
# Zig : https://ziglang.org/download/, dézippé n'importe où ; trouvé dans le PATH ou dans %LOCALAPPDATA%\Programs\zig-*.
# La DLL est écrite dans mh-wilds\updater\bin\ ; release.ps1 -Mod wilds la copie dans release\ (elle n'est recompilée
# que si ce script est relancé : le plugin change rarement).
param([string]$Zig)

$ErrorActionPreference = 'Stop'
if (-not $Zig) {
    $cmd = Get-Command zig -ErrorAction SilentlyContinue
    $Zig = if ($cmd) { $cmd.Source } else {
        Get-ChildItem "$env:LOCALAPPDATA\Programs" -Directory -Filter 'zig-*' -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'zig.exe' } |
            Where-Object { Test-Path $_ } | Select-Object -First 1
    }
}
if (-not $Zig) { throw "zig introuvable : .\build.ps1 -Zig 'C:\...\zig.exe'" }

$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$dll = Join-Path $out 'wilds_dps_meter_updater.dll'
Push-Location $PSScriptRoot
try {
    # -s : sans symboles ni infos de débogage (aucun chemin local dans la DLL)
    & $Zig cc -target x86_64-windows-gnu -shared -O2 -s -fno-ident -o $dll wilds_dps_updater.c -lwinhttp -lbcrypt
    if ($LASTEXITCODE -ne 0) { throw "La compilation a échoué." }
} finally { Pop-Location }
Remove-Item (Join-Path $out '*.lib'), (Join-Path $out '*.pdb') -ErrorAction SilentlyContinue
Write-Host "Compilé : $dll ($((Get-Item $dll).Length) octets)" -ForegroundColor Green
