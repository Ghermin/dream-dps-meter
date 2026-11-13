# DPS meters : notes pour les sessions de développement

Un DPS meter par jeu, chacun dans son dossier, plus un installeur commun. Le README reste court et sans détail technique : tout ce qui est technique va ici.

## Organisation

- `shape-of-dreams/` : Dream DPS Meter (mod du gestionnaire de mods du jeu, `about/` = fichiers du menu Mods).
- `blackveil/` : Blackveil DPS Meter (plugin BepInEx 5).
- `games.json` : la liste des DPS meters, lue par l'installeur et `install.ps1` (fichiers à télécharger, dossier cible, chargeur de mods). **Ajouter un jeu ne demande pas de recompiler l'installeur.**
- `installer/` : l'installeur Windows (`Installer-DPS-Meters.exe`, WinForms .NET Framework 4.8) et `Installer-DPS-Meters.bat` (secours sans fenêtre, lance `install.ps1`).
- `docs/` : la page GitHub (https://ghermin.github.io/dream-dps-meter/), captures dans `docs/screens/`.
- `release/` (racine) : **compatibilité**, copie de la DLL de Shape of Dreams lue par les versions ≤ 1.1.6 du mod. `release.ps1` la tient à jour ; à supprimer quand plus personne n'a ces versions.

## Ajouter un jeu

1. Un dossier `<jeu>/` avec son projet, `src/Version.cs` (`RawBase` = `.../main/<jeu>/`), `release/`.
2. Une entrée dans `games.json` (et un chargeur dans `loaders` s'il en faut un nouveau).
3. Une entrée dans `$mods` de `release.ps1`.
4. Une capture sans info perso dans `docs/screens/`, un bloc `.game` dans `docs/index.html`, une ligne dans le README, et la description de la carte du playground.

## Compiler / publier

```powershell
dotnet build shape-of-dreams -c Release    # copie la DLL dans <Shape of Dreams>\Mods\DreamDpsMeter
dotnet build blackveil -c Release          # copie la DLL dans <Raiders of Blackveil>\BepInEx\plugins\BlackveilDpsMeter
.\release.ps1                              # publie Dream DPS Meter (+1 au dernier chiffre), tag vX.Y.Z
.\release.ps1 -Mod blackveil               # publie Blackveil DPS Meter, tag blackveil-vX.Y.Z
.\release.ps1 -Mod blackveil -NoPush       # compile et prépare sans commit ni push
dotnet build installer -c Release          # installer\bin\Release\net48\Installer-DPS-Meters.exe
```

- Un simple `git push` ne met personne à jour : la mise à jour auto compare `<jeu>/release/version.txt` et télécharge la DLL publiée à côté, que seul `release.ps1` reconstruit (la compilation a besoin des DLL du jeu, donc en local).
- L'installeur est attaché à la release GitHub `installer` (lien fixe) ; il lit `games.json` sur `main`, donc à republier seulement s'il change :
  `gh release upload installer installer\bin\Release\net48\Installer-DPS-Meters.exe --clobber`.
- Tester l'installeur sur le dépôt local : `$env:DPS_METERS_REPO = 'file:///C:/chemin/du/depot/'` avant de le lancer.
- Shape of Dreams, commandes console (mode développeur) : `dps_reset`, `dps_rebuild`, `dps_mock`.
- Blackveil, `Outils = true` dans `BepInEx\config\ghermin.blackveildpsmeter.cfg` : dump de l'interface (`F8`) et captures dans `BepInEx\shots`.

## Infos perso : rien de publié

- Pseudo de jeu, prénom et nom d'utilisateur Windows ne doivent apparaître nulle part (code, DLL, captures, messages de commit). Auteur affiché : `Ghermin`. Identifiants : `ghermin.dreamdpsmeter`, `ghermin.blackveildpsmeter`.
- Les builds Release n'embarquent ni PDB ni chemin local (`DebugType none`, `PathMap`) : garder ces réglages dans chaque `.csproj`.
- Captures (`docs/screens/`, `about/preview.png`, `about/icon.png`) : les regarder en entier avant de les ajouter. Retirer noms de joueurs (au-dessus des personnages, écran de fin, classement), overlays Steam / Discord, autres fenêtres et second écran ; recadrer, flouter fort, réenregistrer sans métadonnées (le script d'export en JPEG le fait). Capture en pleine partie de préférence, pas l'écran de fin.
- Identité des commits : `Ghermin <Ghermin@users.noreply.github.com>` (configurée en local dans le dépôt).

## Bored Dev Playground

Ce projet fait partie du Bored Dev Playground : https://ghermin.github.io/ (repo `Ghermin/Ghermin.github.io`, en local `../Ghermin.github.io`).
Sa page (`docs/index.html`) suit le kit commun : `docs/playground.css` est une copie de `kit/playground.css` du playground, à modifier là-bas puis `sh kit/sync.sh`.

- Local (couleurs, police Chakra Petch) : dans la page, en redéfinissant les variables `--pg-*` après le kit.
- Icône : `brand/icons/dps-meters.svg` du playground ; `node build.js dps-meters` dans `brand/` régénère `docs/favicon.*`, `docs/icon-192.png`, `docs/apple-touch-icon.png` et `installer/app.ico`.
- Si le nom, la description ou les jeux changent, mettre à jour la carte du projet dans `index.html` du playground.
- L'installeur porte le lien « un projet du bored dev playground ».

À garder sur la page : le bandeau `pg-strip`, le thème clair / sombre automatique, le responsive (pas de défilement horizontal sur mobile), 2 polices max (Chakra Petch + JetBrains Mono).
