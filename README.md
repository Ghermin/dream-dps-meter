# Dream DPS Meter — mod pour Shape of Dreams

Un DPS meter intégré au HUD du jeu, avec le même style que l'interface d'origine.
100 % côté client : il ne change rien au gameplay et marche en multi, même si les autres joueurs ne l'ont pas installé.

![Écran de fin](docs/ecran-de-fin.png)

## Installation (une seule commande)

1. Ouvre **PowerShell** (touche Windows, tape `PowerShell`, Entrée).
2. Colle cette ligne et valide :

   ```powershell
   irm https://raw.githubusercontent.com/Ghermin/dream-dps-meter/main/install.ps1 | iex
   ```

3. Lance le jeu → menu **Mods** sur l'écran titre → active **Dream DPS Meter**.

Le script trouve le jeu dans tes bibliothèques Steam et installe le mod dans `Shape of Dreams\Mods\DreamDpsMeter`.

**Mises à jour : rien à faire.** Au lancement du jeu, le mod vérifie s'il existe une nouvelle version sur ce dépôt,
la télécharge et te prévient ; elle s'applique au lancement suivant. (Désactivable dans les options du mod.)

## Ce qu'il affiche

**Pendant la run — en haut à gauche** (même cadre que les objectifs) :
- dégâts totaux de la run, DPS du combat (ou DPS sur le boss pendant un combat de boss), soins, dégâts subis ;
- or et Poussière de Rêve gagnés, répartition des dégâts par élément (barre colorée) ;
- en multi : classement des joueurs par dégâts (une ligne de résumé par joueur).

**Touche Ctrl — au-dessus des caractéristiques** :
- le détail de tes dégâts par mémoire, essence, passif, élément (brûlure, givre…) et attaque de base, avec icônes ;
- soins et boucliers apportés par chaque source (non comptés dans le DPS) ;
- historique : DPS sur chaque boss et DPS moyen par zone.

**Barre de compétences** : un petit % au-dessus de chaque mémoire et sous chaque essence (part de tes dégâts).

**Écran de fin** :
- dégâts et part de chaque mémoire / essence directement sur le panneau du héros ;
- lignes « DPS moyen en combat », « Meilleur DPS sur un combat », « DPS moyen sur les boss » dans la liste de stats ;
- « Dégâts par source » : toutes les sources de la run, y compris les essences et mémoires abandonnées en route (grisées).

## Options

Dans le gestionnaire de mods du jeu : durée avant fin de combat, overkill compté ou non, inclinaison du panneau,
mises à jour automatiques.

Commandes console (mode développeur) : `dps_reset`, `dps_rebuild`, `dps_mock` (données de test).

## Développement

Le projet se trouve directement dans `Shape of Dreams\Mods\DreamDpsMeter` (il référence les DLL du jeu via `..\..`).

```powershell
dotnet build -c Release      # compile et copie DreamDpsMeter.dll à la racine du mod
.\release.ps1 1.2.0          # publie une version : DLL dans release\, version.txt, commit, tag, push
```

Avec le mode développeur et l'auto-reload du gestionnaire de mods, chaque compilation est rechargée en jeu.
