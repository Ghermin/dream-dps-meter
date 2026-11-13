@echo off
rem Installation sans fenetre (secours) : telecharge et lance la derniere version de install.ps1 depuis GitHub.
rem L'installeur normal est Installer-DPS-Meters.exe.
title Installation des DPS meters
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072; try { irm 'https://raw.githubusercontent.com/Ghermin/dream-dps-meter/main/install.ps1' | iex } catch { Write-Host ''; Write-Host ('  Erreur : ' + $_.Exception.Message) -ForegroundColor Red }"
echo.
echo   Tu peux fermer cette fenetre.
pause >nul
