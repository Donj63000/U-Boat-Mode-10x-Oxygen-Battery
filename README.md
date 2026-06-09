<p align="center">
  <img src="Images%20GITHUB/affiche1.png" alt="Long Submerged 10x+ UBOAT mod banner" width="100%">
</p>

<h1 align="center">Long Submerged 10x+</h1>

<p align="center">
  <strong>A UBOAT gameplay mod for long underwater patrols, infinite battery, runtime tuning, SuperSpeed, and Mega Torpedoes.</strong>
</p>

<p align="center">
  <kbd>UBOAT 2026.1 Patch 20</kbd>
  <kbd>F10 in-game menu</kbd>
  <kbd>Ready package in mod-here</kbd>
  <kbd>Save compatible after restart</kbd>
</p>

<p align="center">
  <a href="mod-here/"><strong>Install Package</strong></a>
  &nbsp;|&nbsp;
  <a href="mod-here/install.txt"><strong>Beginner Install Guide</strong></a>
  &nbsp;|&nbsp;
  <a href="#f10-menu"><strong>F10 Menu</strong></a>
  &nbsp;|&nbsp;
  <a href="#developer-build"><strong>Developer Build</strong></a>
</p>

---

## Player Highlights

| Feature | Default profile | What it changes |
| --- | --- | --- |
| **Infinite Battery** | `Battery = 100` | Holds the submarine energy resource charged during gameplay. |
| **Long Oxygen** | `Oxygen = 100` | Targets about **90 days underwater** instead of only a few days. |
| **Mega Torpedoes** | `Torpedoes = 10` | Scales damage, crew damage, blast radius, visual blast radius, and explosion intensity. |
| **SuperSpeed** | `SuperSpeed = 3.5` | Boosts the two fastest forward gears for faster travel. |
| **Runtime Menu** | `F10` | Lets you tune the mod in game without rebuilding the mod files. |

> [!IMPORTANT]
> The player-ready mod is inside [`mod-here/LongSubmerged10x`](mod-here/LongSubmerged10x). Copy that full folder into your UBOAT `Mods` directory.

<p align="center">
  <img src="Images%20GITHUB/affiche2.png" alt="Long Submerged 10x+ feature overview" width="100%">
</p>

## Quick Install

1. Close UBOAT completely.
2. Open the packaged mod folder:

   ```text
   mod-here/LongSubmerged10x
   ```

3. Copy the whole `LongSubmerged10x` folder into:

   ```text
   %USERPROFILE%\AppData\LocalLow\Deep Water Studio\UBOAT\Mods\
   ```

4. Your final path should look like this:

   ```text
   %USERPROFILE%\AppData\LocalLow\Deep Water Studio\UBOAT\Mods\LongSubmerged10x\
   ```

5. Start the UBOAT launcher, enable **Long Submerged 10x+**, then launch the game.

For a more beginner-friendly walkthrough, read [`mod-here/install.txt`](mod-here/install.txt).

## F10 Menu

Press `F10` after loading a save. Press `F10` again or `Escape` to close the menu.

| Control | Normal value | Default value | Maximum value |
| --- | ---: | ---: | ---: |
| **Battery** | `1` | `100` | `100`, infinite battery hold |
| **Oxygen** | `1` | `100` | `100`, about 90 days underwater |
| **SuperSpeed** | `1` | `3.5` | `100` |
| **Torpedoes** | `1` | `10` | `100` |

The menu includes:

- **Default**: restores the shipped profile.
- **Reapply now**: reapplies the current values immediately in the loaded game.
- Saved settings through `PlayerPrefs`, so your tuning persists between sessions.

## Mega Torpedoes

Mega Torpedoes are designed to feel decisive without requiring spreadsheet edits while you test.

| Runtime behavior | Included |
| --- | --- |
| Torpedo damage scaling | Yes |
| Crew damage scaling | Yes |
| Explosion radius scaling | Yes |
| Visual explosion radius scaling | Yes |
| Explosion intensity scaling | Yes |
| Reliability failure prevention | Yes, while Mega Torpedoes are active |
| Locked-target guidance assistance | Yes, applied at runtime |

## Recommended Load Order

Place **Long Submerged 10x+** after other mods that edit these files:

- `General.xlsx`
- `Entities.xlsx`
- `U-boat.xlsx`

This helps the runtime and generated sheets keep the intended values.

## Mod Package

The installable package is tracked in [`mod-here/`](mod-here/):

```text
mod-here/
  install.txt
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

## Troubleshooting

| Problem | Fix |
| --- | --- |
| The F10 menu does not open | Fully close UBOAT, delete the UBOAT local `Temp` cache, then restart the game. |
| The mod does not appear in the launcher | Check that the folder is not nested as `LongSubmerged10x/LongSubmerged10x`. |
| Battery still behaves normally | Make sure the F10 menu shows `Battery = 100`, then click **Reapply now**. |
| Another mod overrides values | Move **Long Submerged 10x+** lower in the launcher load order. |

## Developer Build

The generator is included for maintainers:

```powershell
python -m pip install -r requirements.txt
python .\build_uboat_long_submerged_mod.py --uboat "C:\Program Files (x86)\Steam\steamapps\common\UBOAT" --force --clear-cache
```

Important defaults:

| Setting | Default |
| --- | ---: |
| `--oxygen-consumption-factor` | `1800` |
| `--battery-capacity-factor` | `10` |
| `--energy-usage-factor` | `0.1` |
| `--fast-speed-factor` | `3.5` |
| `--fast-speed-fuel-factor` | `8` |
| `--player-submarine-max-speed` | `45` |
| `--torpedo-damage-factor` | `10` |
| `--torpedo-explosion-radius-factor` | `10` |
| `--torpedo-explosion-intensity-factor` | `10` |

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
- F10 settings are runtime settings, so players can test values in game before keeping their favorite profile.
