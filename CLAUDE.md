# DPS meters : notes pour les sessions de développement

Un DPS meter par jeu, chacun dans son dossier, plus un installeur commun. Le README reste court et sans détail technique : tout ce qui est technique va ici.

## Organisation

- `shape-of-dreams/` : Dream DPS Meter (mod du gestionnaire de mods du jeu, `about/` = fichiers du menu Mods).
- `blackveil/` : Blackveil DPS Meter (plugin BepInEx 5).
- `mh-wilds/` : Wilds DPS Meter, script Lua REFramework pour Monster Hunter Wilds (voir plus bas).
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

## Monster Hunter Wilds (`mh-wilds/`)

- Chargeur : REFramework (`dinput8.dll`), nightly `REFramework-nightly/releases/latest/download/REFramework.zip` (DLL unique pour tous les jeux RE Engine ; la release stable date de mars 2025 et ne suit plus les patchs du jeu). Déjà présent : pas réinstallé.
- `src/wilds_dps_meter.lua` (seul fichier lu dans `reframework\autorun`, qui n'est pas parcouru récursivement) fait `require("wilds_dps_meter.core")` ; le code est dans `src/wilds_dps_meter/`.
- `autorun` est partagé avec les autres mods : dans `games.json`, `target` = notre sous-dossier, et le point d'entrée est dans `extra` (chemins relatifs au jeu, retirés un par un à la désinstallation). Ne jamais mettre `target` sur `reframework\autorun`.
- Pas de mise à jour auto : le Lua de REFramework n'a ni réseau ni `os.execute`. `"autoUpdate": false` : l'installeur compare `<target>\version.txt` à la dernière version et propose « Mettre à jour ».
- `.\mh-wilds\deploy.ps1` copie `src` dans le jeu ; en jeu, menu REFramework (Inser) > ScriptRunner > Reset Scripts.
- Tester sans combat : bouton « Démo » du menu, ou `"demo": true` dans `reframework\data\wilds_dps_meter.json` avant le lancement. Le script écrit dans `re2_framework_log.txt` (dossier du jeu) une ligne au chargement (hook et types trouvés) et une au premier affichage du panneau (ou la première erreur de dessin). Les `[error]` de `Scan.cpp` / `IntegrityCheckBypass` viennent de REFramework et sont sans conséquence.
- Le panneau est dessiné à la main (draw list ImGui dans une fenêtre sans fond ni titre) : il ne se déplace à la souris que menu REFramework ouvert.
- `.\release.ps1 -Mod wilds` copie `src` dans `release/` (tag `wilds-vX.Y.Z`).
Ce qui a été vérifié en jeu (solo), avec des sondes (`mh-wilds/dev/wilds_dps_probe.lua`, jamais publiée) :
- Dégâts : après `app.EnemyCharacter.evHit_Damage(app.HitInfo)`, `HitInfo:get_DamageData().FinalDamage` = perte de PV exacte (les PV ne baissent qu'ensuite, dans `evDamage_Health` : lire les PV avant/après le hook ne donne rien). Attaquant : `HitInfo:getActualAttackOwner()` (« MasterPlayer » = soi). Coups de monstre à monstre et décor (« Gm… ») ignorés.
- Palico : `app.OtomoCharacter:get_OwnerHunterCharacter()`, ses dégâts vont à son chasseur.
- Grand monstre : `EnemyCharacter._Context._Em:get_IsBoss()` ; mort : `get_HealthMgr():get_IsDead()` (vérifié à chaque image).
- Détail d'un coup : `app.cEnemyStockDamage.mcEnemyHitMarkManager.playHitMarkEffect(cCalcDamage, HitInfo)`, champs `Physical`, `Element` (dégâts) et `Poison`, `Paralyse`, `Sleep`, `Blast`, `Stun`, `Stamina`, `Ride` (accumulation, pas des dégâts). Élément de l'arme : `HunterCharacter:get_HunterStatus():get_AttackPower():get_AttibuteType()` (sic), enum `app.WeaponDef.ATTR`.
- Jauges du monstre : `_Em.Conditions` (un champ, pas une méthode : lire via méthode puis champ / `<X>k__BackingField`) `._Conditions[]` de types `app.cEnemyBadCondition<X>` (`get_IsValid`, `get_IsActive`, `get_Value`/`get_LimitValue`, `get_CurrentTimer`/`get_ActivateTime`) ; épuisement à part : `Conditions.Tired` (`get_Stamina`/`get_DefaultStamina`).
- Menu ouvert (Start, carte, coffre de fin) : `app.GUIManager:get_IsHighHudInput()` vaut **true** quand un menu prend la main.
- Poison / nitro (dégâts sans coup, `evDamage(cApplyEventParam)` par nom de `DamageType`) : pas encore vu en jeu. Multi : pas encore testé (coups des autres joueurs, total officiel `app.cQuestFlowParam.setSharedQuestAwardInfo(userIndex, cQuestAwardSync).award05`).
- Réglages dans le **vrai menu Options** (`src/wilds_dps_meter/options_menu.lua`) : Options > Paramètres de jeu > section « DPS meter ».
  - Données : `VariousDataManager._Setting._MenuSetting._OptionData` : `Values[]` (liste affichée, dans l'ordre) et `_DataTable[]` (400 cases, index = identifiant). Section = entrée `HEADLINE` (type 4), réglage = entrée `DEFAULT` (0) avec `_Items` (choix), rattachée par `<ParentOptionID>`. Nos entrées sont des `MemberwiseClone` d'entrées du jeu, `add_ref_permanent` obligatoire.
  - Identifiants : `FREE_394`…`FREE_399` (réservés, inutilisés, même valeur interne et fixe). Sauvegarde : `SaveDataManager:get_SystemSaveData()._SystemCommon._Option.Values` (400 cases, index = identifiant fixe, -1 = jamais réglé) : le jeu y enregistre nos valeurs (vérifié : conservées après redémarrage). Lecture / écriture : `OptionUtil.getOptionValue` / `setOptionValue`.
  - Textes : le menu les résout en natif (aucune fonction managée appelée, `via.gui.message.get` inutile). On écrit après le jeu : `GUI030100.DispData.setTitle/updateTitle/setHelp/setSettingText` (reçoivent le `via.gui.Text`, ligne identifiée par `get_OptionId`) et `GUI030100PartsDetail.updateItem(index, SelectItem, …)` (choix de droite : `txt_subIndex`, `PNL_FluentSize/txt_subSentence`). `via.gui.Text:set_Message(String)`.
  - Onglets : `GUI030100._TitleCategoryDataTable` / `_InGameCategoryDataTable`, `CategoryData { MessageId, Icon (app.IconDef.FACILITY, 122 icônes), Category }`. Pas encore d'onglet à nous.
  - Langue : `OptionUtil.getTextLanguage()` (FRENCH = 2) ne vaut la langue du joueur qu'après le chargement de ses options : relue chaque seconde.
- Dev : `mh-wilds/dev/wilds_dev_loader.lua` recharge un module de test sans « Reset scripts » (commande dans `reframework\data\wilds_dev.json`). Fenêtre du jeu : ne **jamais la redimensionner** de l'extérieur (le jeu, non DPI-aware, calcule une hauteur absurde et le pilote plante) ; la déplacer seulement, taille réglée dans `config.ini` (`WindowMode=Normal`, `NormalWindowResolution`).
- Sondes : n'appeler **que** des lectures (`get_…`, `is…`). Une sonde qui appelait toute méthode sans paramètre a exécuté `MissionManager.setActiveQuestLayout` en pleine quête (partie déréglée, jeu à relancer).
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
