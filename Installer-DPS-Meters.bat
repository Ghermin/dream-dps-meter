@echo off
rem Installateur en un double-clic des DPS meters de Ghermin (Shape of Dreams / Raiders of Blackveil).
rem Il telecharge et lance la derniere version de install.ps1 depuis GitHub : ce fichier n'a jamais besoin d'etre mis a jour.
title Installation des DPS meters de Ghermin
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072; try { irm 'https://raw.githubusercontent.com/Ghermin/dream-dps-meter/main/install.ps1' | iex } catch { Write-Host ''; Write-Host ('  Erreur : ' + $_.Exception.Message) -ForegroundColor Red }"
echo.
echo   Tu peux fermer cette fenetre.
pause >nul
