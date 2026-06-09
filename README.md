# Long Submerged 10x+ for UBOAT

![Long Submerged 10x+ UBOAT mod banner](Images%20GITHUB/affiche1.png)

An unofficial gameplay mod for **UBOAT 2026.1 Patch 20** focused on long underwater patrols, runtime tuning, boosted fast travel, and stronger torpedoes.

The ready-to-install player package is in [`mod-here/`](mod-here/).

## Main Features

- **Infinite battery by default**
  - The F10 menu ships with `Battery = 100`.
  - At `100`, the runtime cuts positive electrical drain and keeps the submarine `Energy` resource charged.
  - The slider remains adjustable from `1` to `100`, where `1` restores normal behavior.
- **Long underwater oxygen**
  - Default oxygen profile targets about **90 days** underwater.
  - The F10 `Oxygen` slider can be adjusted from normal behavior to the full long-patrol profile.
- **F10 runtime menu**
  - Toggle and tune `Mega Batterie`, `Mega Oxygene`, `SuperVitesse`, and `Mega Torpilles`.
  - Sliders are saved with `PlayerPrefs`.
  - `Default` restores the shipped profile.
  - `Reapply now` applies values immediately during the current game.
- **SuperSpeed**
  - Boosts the two fastest forward gears.
  - Default speed factor is `3.5`.
  - The F10 slider can go from `1` to `100`.
- **Mega Torpedoes**
  - Default torpedo factor is `10`.
  - Damage, crew damage, blast radius, visual effect radius, and explosion intensity are runtime-scaled.
  - Torpedo reliability failures are disabled while Mega Torpedoes are active.
  - Locked-target torpedoes receive runtime guidance assistance.

![Long Submerged 10x+ feature overview](Images%20GITHUB/affiche2.png)

## Easy Install

For normal players, use the packaged mod folder:

```text
mod-here/
```

Copy this folder:

```text
mod-here/LongSubmerged10x
```

Into your UBOAT mods directory:

```text
%USERPROFILE%\AppData\LocalLow\Deep Water Studio\UBOAT\Mods\
```

The final path should be:

```text
%USERPROFILE%\AppData\LocalLow\Deep Water Studio\UBOAT\Mods\LongSubmerged10x\
```

Then start the UBOAT launcher, enable **Long Submerged 10x+**, and place it after other mods that edit `General.xlsx`, `Entities.xlsx`, or `U-boat.xlsx`.

See [`mod-here/install.txt`](mod-here/install.txt) for a step-by-step beginner install guide.

## In Game

Press `F10` after loading a save.

Default shipped profile:

- `Battery`: `100`, infinite battery hold.
- `Oxygen`: `100`, about 90 days underwater.
- `SuperSpeed`: `3.5`, boosted fast gears.
- `Torpedoes`: `10`, stronger explosions and better locked-target behavior.

Press `Escape` or `F10` to close the menu.

## Mod Package

The active mod folder contains:

```text
LongSubmerged10x/
  Manifest.json
  README_LongSubmerged10x.txt
  LongSubmerged10x_generation_report.txt
  Source/
    LongSubmergedRuntimePatch.cs
  Data Sheets/
    General.xlsx
    Realistic Travel/General.xlsx
    Entities.xlsx
```

UBOAT compiles the runtime patch when the mod is loaded.

## Developer Build

The generator is included for maintainers:

```powershell
python -m pip install -r requirements.txt
python .\build_uboat_long_submerged_mod.py --uboat "C:\Program Files (x86)\Steam\steamapps\common\UBOAT" --force --clear-cache
```

Important defaults:

- `--oxygen-consumption-factor 1800`
- `--battery-capacity-factor 10`
- `--energy-usage-factor 0.1`
- `--fast-speed-factor 3.5`
- `--fast-speed-fuel-factor 8`
- `--player-submarine-max-speed 45`
- `--torpedo-damage-factor 10`
- `--torpedo-explosion-radius-factor 10`
- `--torpedo-explosion-intensity-factor 10`

## Validation

Current checks used during development:

```powershell
python -m unittest discover -s tests -v
python -m py_compile .\build_uboat_long_submerged_mod.py .\tests\test_long_submerged_generator.py
git diff --check
```

The generated runtime patch is also compiled manually against UBOAT and Unity managed DLLs, including `UnityEngine.UI.dll`, before release.

## Notes

- This is an unofficial mod.
- The mod is designed for **UBOAT 2026.1 Patch 20**.
- Existing saves are supported after a full game restart.
- If the F10 menu does not open, fully close UBOAT and delete the UBOAT local `Temp` cache before relaunching.
