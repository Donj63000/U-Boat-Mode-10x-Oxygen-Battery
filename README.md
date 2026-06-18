<p align="center">
  <img src="Images%20GITHUB/affiche1.png" alt="Long Submerged 10x+ UBOAT mod banner" width="100%">
</p>

<h1 align="center">Long Submerged 10x+</h1>

<p align="center">
  <strong>A UBOAT gameplay mod for long underwater patrols, infinite battery, runtime tuning, SuperSpeed, Mega Torpedoes, Mega Sonar, Heavy Armor, and Super discrétion / Super Stealth.</strong>
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
| **Mega Sonar** | `Sonar = 3` | Scales hydrophone listening range while leaving arcs and surface modifiers alone. |
| **Heavy Armor x3** | `Heavy Armor = off` | Optional F10 toggle that reduces player submarine damage to about one third while keeping leaks, fires, repairs, pressure, and destruction possible. |
| **Super discrétion / Super Stealth** | `Super discrétion = off` | When enabled, reduces player submarine noise and detectability to about one third without making it invisible. |
| **SuperSpeed** | `SuperSpeed = 8` | Boosts the two fastest forward gears for faster travel. |
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

<p align="center">
  <img src="Images%20GITHUB/f10-menu-blindage-green-light.png" alt="F10 menu with Heavy Armor x3 and green SilentRun interior lighting" width="100%">
</p>

<p align="center">
  <img src="Images%20GITHUB/f10-oxygen-battery-settings.png" alt="F10 menu showing infinite battery and 90 day oxygen settings" width="100%">
</p>

<p align="center">
  <img src="Images%20GITHUB/f10-battery-setting-closeup.png" alt="F10 Mega Battery setting close-up showing infinite battery" width="70%">
</p>

| Control | Normal value | Default value | Maximum value |
| --- | ---: | ---: | ---: |
| **Battery** | `1` | `100` | `100`, infinite battery hold |
| **Oxygen** | `1` | `100` | `100`, about 90 days underwater |
| **SuperSpeed** | `1` | `8` | `20` |
| **Torpedoes** | `1` | `10` | `10` |
| **Sonar** | `1` | `3` | `10` |
| **Heavy Armor x3** | `off` | `off` | `on`, x3 protection |
| **Super discrétion / Super Stealth** | `off` | `off` | `on`, x3 stealth |

The menu includes:

- **Default**: restores the shipped profile.
- **Reapply now**: reapplies the current values immediately in the loaded game.
- Saved settings through `PlayerPrefs`, so your tuning persists between sessions.
- **Heavy Armor x3**: optional toggle that reduces incoming player submarine and crew damage by x3. It is off by default, and migration settings v16 / v1.4.16 turns old saved Heavy Armor settings off once during migration. Crush depth remains vanilla and can still be fatal.
- **Super discrétion / Super Stealth**: optional toggle that divides player noise and detectability by x3. Enemy contacts and detection mechanics still exist.

## Heavy Armor x3

Heavy Armor is a manual F10 mode for players who want a tougher submarine without making it immortal.

What is included:

- Reduces player submarine equipment and hull damage to about one third.
- Reduces player crew damage when the damage event explicitly targets the player ship.
- Reduces flaw and fire chances together with player equipment damage.
- Keeps pressure damage and crush depth vanilla, so diving too deep can still be fatal.
- Keeps explosions scoped to the player ship instead of globally weakening every target in a shared blast.
- Keeps the mode disabled by default; migration settings v16 / v1.4.16 turns old saved Heavy Armor settings off once, then preserves later F10 choices.

## Interior Lighting

The runtime lighting patch keeps the original UBOAT modes but changes their visual colors: Alarm red renders as amber orange, and SilentRun blue renders as submarine green.

<p align="center">
  <img src="Images%20GITHUB/silentrun-green-light.png" alt="SilentRun green interior lighting in game" width="100%">
</p>

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
| `--oxygen-consumption-factor` | `250` |
| `--battery-capacity-factor` | `10` |
| `--energy-usage-factor` | `0.1` |
| `--fast-speed-factor` | `8` |
| `--fast-speed-fuel-factor` | `8` |
| `--player-submarine-max-speed` | `45` |
| `--torpedo-damage-factor` | `10` |
| `--torpedo-crew-damage-factor` | `10` |
| `--torpedo-explosion-radius-factor` | `3` |
| `--torpedo-explosion-intensity-factor` | `3` |

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
