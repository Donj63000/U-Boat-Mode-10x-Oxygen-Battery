# UBOAT - Long Submerged 10x+

Mod pour **UBOAT 2026.1 Patch 20** qui permet de rester immergé beaucoup plus longtemps sans casser la batterie ni la ventilation.

## Ce que fait le mod

- Batterie beaucoup plus longue :
  - capacité des accumulateurs multipliée par 10 ;
  - consommation électrique des équipements principaux réduite ;
  - recharge/production batterie laissée vanilla pour éviter une recharge instantanée.
- Air / oxygène beaucoup plus long :
  - `Oxygen Consumption Per Character` est divisé par 125 ;
  - le mod vise environ 7 à 8 jours d'air sur une sauvegarde déjà en cours.
- Vitesse rapide améliorée :
  - les deux crans avant les plus rapides renforcent les vitesses attendues et la propulsion réelle ;
  - le plafond runtime et datasheet des sous-marins joueur est relevé à 45 km/h ;
  - la consommation carburant des deux crans rapides est augmentée pour garder une autonomie logique ;
  - les Type IX officiels du DLC Steam sont inclus quand le DLC est installé ;
  - les vitesses lentes et mi-vitesse restent vanilla pour garder la manoeuvre fine.
- Discipline et fatigue adaptées à l'immersion longue :
  - les pertes sous l'eau sont réduites proportionnellement.
- Ventilation laissée vanilla :
  - la ligne `Ventilation` n'est plus modifiée par défaut pour éviter les bugs vus dans les essais précédents.
- Patch runtime AirFix :
  - UBOAT garde parfois l'ancien modifier d'oxygène sur une sauvegarde existante ;
  - le patch Harmony force le recalcul de `OxygenBreathModifier` après chargement, `Awake`, ajout ou retrait d'équipage ;
  - le patch Harmony corrige aussi le plafond de vitesse, les propulseurs et les consommations rapides sur sauvegarde existante.

## Installation rapide

1. Fermer complètement UBOAT.
2. Télécharger ce dépôt en ZIP ou cloner le dépôt.
3. Copier le dossier `LongSubmerged10x` dans :

```text
%USERPROFILE%\AppData\LocalLow\Deep Water Studio\UBOAT\Mods\
```

4. Lancer UBOAT.
5. Activer **Long Submerged 10x+** dans le launcher.
6. Le placer après les autres mods qui modifient `General.xlsx`, `Entities.xlsx` ou `U-boat.xlsx`.
7. Charger une sauvegarde existante ou lancer une nouvelle carrière.

Après le premier lancement, UBOAT compile les fichiers du mod et régénère son cache `Data Sheets`.

## Test en jeu

Pour vérifier que le mod fonctionne :

1. Charger une partie.
2. Plonger avec une qualité d'air proche de 100 %.
3. Ouvrir le tooltip de qualité de l'air.
4. La durée ne doit plus rester autour de 13 heures.
5. Si le tooltip affiche encore `Équipage -4/min`, fermer UBOAT, vérifier que le mod est bien activé et placé après les autres mods, puis relancer.

## Générer le mod depuis les sources

Le dossier prêt à installer est déjà inclus dans `LongSubmerged10x/`. Pour le régénérer depuis les fichiers vanilla de ton installation UBOAT :

```powershell
python -m pip install -r requirements.txt
python .\build_uboat_long_submerged_mod.py --uboat "C:\Program Files (x86)\Steam\steamapps\common\UBOAT" --force --clear-cache
```

Options principales :

- `--oxygen-consumption-factor 125` : divise la consommation d'oxygène par 125.
- `--battery-capacity-factor 10` : multiplie les accumulateurs par 10.
- `--energy-usage-factor 0.1` : réduit seulement la consommation électrique positive des équipements.
- Les lignes `EnergyUsage` négatives, utilisées pour la recharge/production batterie, restent vanilla.
- `--fast-speed-factor 3.5` : renforce la vitesse attendue et la propulsion des crans rapides de marche avant.
- `--fast-speed-fuel-factor 8` : augmente la consommation carburant des crans rapides.
- `--fast-speed-top-gears 2` : applique le boost uniquement aux deux derniers crans avant.
- `--player-submarine-max-speed 45` : relève le plafond des sous-marins joueur à 45 km/h.
- Le générateur lit aussi `UBOAT_Data/StreamingAssets/Packages/uboat.dlc.type-ix/Data Sheets` quand le DLC Type IX officiel est présent.
- `--clear-cache` : vide le cache local UBOAT `Data Sheets` pour forcer la recompilation.

## Structure du dépôt

- `LongSubmerged10x/` : mod prêt à installer.
- `build_uboat_long_submerged_mod.py` : générateur officiel actuel.
- `tests/` : tests unitaires du générateur.
- `tools/AssemblyInspector/` : outil local d'inspection IL utilisé pour comprendre le chargement des datasheets et le recalcul d'oxygène.

## Vérifications effectuées

- Tests unitaires Python : `python -m unittest discover -s tests -v`.
- Compilation du patch runtime C# contre les DLL UBOAT :
  - `com.uboat.game.dll`
  - `0Harmony.dll`
  - `UnityEngine.dll`
  - `UnityEngine.CoreModule.dll`
- Vérification des valeurs générées dans :
  - `Data Sheets/General.xlsx`
  - `Data Sheets/Realistic Travel/General.xlsx`
  - `Data Sheets/Entities.xlsx`

## Notes

Ce mod est prévu pour **UBOAT 2026.1 Patch 20**. Il peut fonctionner sur d'autres versions 2026.1, mais les datasheets et les assemblies du jeu peuvent changer après une mise à jour.

Si un autre mod touche l'air, la batterie ou les mêmes fichiers datasheets, l'ordre de chargement est important.
