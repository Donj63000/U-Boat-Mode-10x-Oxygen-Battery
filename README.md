<p align="center">
  <img src="Images%20GITHUB/affiche1.png" alt="Long Submerged 10x+ UBOAT mod banner" width="100%">
</p>

<h1 align="center">Long Submerged 10x+</h1>

<p align="center">
  <strong>A polished UBOAT gameplay mod for long underwater patrols, infinite battery, runtime tuning, SuperSpeed, Mega Torpedoes, Mega Sonar, DeepDive, Heavy Armor, and Super discrétion / Super Stealth.</strong>
</p>

<p align="center">
  <img alt="UBOAT 2026.1 Patch 20" src="https://img.shields.io/badge/UBOAT-2026.1%20Patch%2020-102231?style=for-the-badge">
  <img alt="Mod version 1.4.16" src="https://img.shields.io/badge/Mod-v1.4.16-27543f?style=for-the-badge">
  <img alt="Runtime F10 menu" src="https://img.shields.io/badge/Runtime-F10%20Menu-7d5a2f?style=for-the-badge">
  <img alt="Ready package" src="https://img.shields.io/badge/Package-Ready-2f4f6f?style=for-the-badge">
</p>

<table>
  <tr>
    <td align="center" width="25%">
      <a href="mod-here/LongSubmerged10x"><strong>Install Package</strong></a>
      <br>
      <sub>Ready-to-copy mod folder</sub>
    </td>
    <td align="center" width="25%">
      <a href="mod-here/install.txt"><strong>Install Guide</strong></a>
      <br>
      <sub>Beginner-friendly steps</sub>
    </td>
    <td align="center" width="25%">
      <a href="#f10-command-menu"><strong>F10 Menu</strong></a>
      <br>
      <sub>Runtime tuning in game</sub>
    </td>
    <td align="center" width="25%">
      <a href="#developer-build"><strong>Developer Build</strong></a>
      <br>
      <sub>Generator and validation</sub>
    </td>
  </tr>
</table>

---

<table>
  <tr>
    <td align="center" width="25%"><strong>90 days</strong><br><sub>target underwater oxygen profile</sub></td>
    <td align="center" width="25%"><strong>Infinite</strong><br><sub>battery hold while Mega Batterie is active</sub></td>
    <td align="center" width="25%"><strong>x10</strong><br><sub>runtime Mega Torpedoes damage profile</sub></td>
    <td align="center" width="25%"><strong>F10</strong><br><sub>in-game tuning without rebuilding files</sub></td>
  </tr>
</table>

<p align="center">
  <img src="Images%20GITHUB/affiche2.png" alt="Long Submerged 10x+ feature overview" width="100%">
</p>

> [!IMPORTANT]
> The player-ready mod is inside [`mod-here/LongSubmerged10x`](mod-here/LongSubmerged10x). Copy that full folder into your UBOAT `Mods` directory.

## Install First

<table>
  <tr>
    <td width="33%">
      <strong>1. Close UBOAT</strong>
      <br>
      <sub>Exit the game and launcher completely before copying files.</sub>
    </td>
    <td width="33%">
      <strong>2. Copy the folder</strong>
      <br>
      <code>mod-here/LongSubmerged10x</code>
    </td>
    <td width="33%">
      <strong>3. Enable the mod</strong>
      <br>
      <sub>Activate Long Submerged 10x+ in the UBOAT launcher.</sub>
    </td>
  </tr>
</table>

1. Close UBOAT completely.
2. Copy the full `LongSubmerged10x` folder from:

   ```text
   mod-here/LongSubmerged10x
   ```

3. Paste it into:

   ```text
   %USERPROFILE%\AppData\LocalLow\Deep Water Studio\UBOAT\Mods\
   ```

4. Your final path should look like this:

   ```text
   %USERPROFILE%\AppData\LocalLow\Deep Water Studio\UBOAT\Mods\LongSubmerged10x\
   ```

5. Start the UBOAT launcher, enable **Long Submerged 10x+**, then launch the game.

For a more beginner-friendly walkthrough, read [`mod-here/install.txt`](mod-here/install.txt).

## What This Mod Changes

<table>
  <tr>
    <td width="50%">
      <strong>Endurance</strong>
      <br>
      Long oxygen and infinite battery turn submerged patrols into long-range operations instead of short emergency dives.
    </td>
    <td width="50%">
      <strong>Runtime control</strong>
      <br>
      The F10 menu lets you tune battery, oxygen, speed, torpedoes, sonar, stealth, armor, depth, lighting, and reinforcements.
    </td>
  </tr>
  <tr>
    <td width="50%">
      <strong>Combat</strong>
      <br>
      Mega Torpedoes and Mega Sonar are runtime systems, so the profile can be changed in game without rebuilding the package.
    </td>
    <td width="50%">
      <strong>Compatibility mindset</strong>
      <br>
      Runtime hooks are scoped, generated data sheets stay deterministic, and disabled toggles return important behavior to vanilla.
    </td>
  </tr>
</table>

## Player Highlights

| Feature | Default profile | What it changes |
| --- | --- | --- |
| **Infinite Battery** | `Battery = 100` | Holds the submarine energy resource charged during gameplay. |
| **Long Oxygen** | `Oxygen = 100` | Targets about **90 days underwater** instead of only a few days. |
| **Mega Torpedoes** | `Torpedoes = 10` | Scales damage, crew damage, blast radius, visual blast radius, and explosion intensity. |
| **Mega Sonar** | `Sonar = 3` | Scales hydrophone listening range while leaving arcs and surface modifiers alone. |
| **Heavy Armor x3** | `Heavy Armor = off` | Optional F10 toggle that reduces player submarine damage to about one third while keeping leaks, fires, repairs, pressure, and destruction possible. |
| **DeepDive** | `Plongée x2 = on` | Optional F10 toggle, enabled by default, that doubles depth commands above 10 m, evaluates crew depth stress on half real depth, targets 600 m operational real depth, and keeps fatal crush at 700 m. |
| **Interior Lighting** | `Couleurs eclairage = on` | Optional F10 lighting toggle, enabled by default, with F10 color dropdowns for Alarm and SilentRun. Defaults remain amber orange and submarine green. |
| **Super discrétion / Super Stealth** | `Super discrétion = off` | When enabled, reduces player submarine noise and detectability to about one third without making it invisible. |
| **SuperSpeed** | `SuperSpeed = 8` | Boosts the two fastest forward gears for faster travel. |
| **Runtime Menu** | `F10` | Lets you tune the mod in game without rebuilding the mod files. |

## Visual Preview

<table>
  <tr>
    <td width="50%">
      <img src="Images%20GITHUB/f10-menu-blindage-green-light.png" alt="F10 menu with Heavy Armor x3 and green SilentRun interior lighting" width="100%">
      <br>
      <sub><strong>Runtime command menu:</strong> Heavy Armor, DeepDive, stealth, lighting, sonar, torpedoes, oxygen, and battery controls.</sub>
    </td>
    <td width="50%">
      <img src="Images%20GITHUB/f10-oxygen-battery-settings.png" alt="F10 menu showing infinite battery and 90 day oxygen settings" width="100%">
      <br>
      <sub><strong>Endurance profile:</strong> Mega Batterie and Mega Oxygene are tuned directly from F10.</sub>
    </td>
  </tr>
  <tr>
    <td width="50%">
      <img src="Images%20GITHUB/silentrun-green-light.png" alt="SilentRun green interior lighting in game" width="100%">
      <br>
      <sub><strong>Interior lighting presets:</strong> SilentRun defaults to submarine green.</sub>
    </td>
    <td width="50%">
      <img src="Images%20GITHUB/f10-battery-setting-closeup.png" alt="F10 Mega Battery setting close-up showing infinite battery" width="100%">
      <br>
      <sub><strong>Mega Batterie:</strong> the runtime profile can hold battery at maximum while active.</sub>
    </td>
  </tr>
</table>

## Latest Changes

> [!NOTE]
> `mod-here/LongSubmerged10x` includes the refreshed runtime patch, manifest, report, generated README, and data sheets for the current build.

- **DeepDive stress scaling**: when `Plongée x2` is enabled, the crew depth-stress calculation now uses half of the real depth. The submarine can still dive to 600 m operational depth and still crushes at 700 m, but the stress thresholds now match the x2 depth mode instead of triggering too early.
- **Generated package updated**: the tracked installable package is ready to copy into the UBOAT `Mods` folder.

## F10 Command Menu

Press `F10` after loading a save. Press `F10` again or `Escape` to close the menu.

<table>
  <tr>
    <td align="center" width="20%"><strong>Battery</strong><br><sub>infinite hold</sub></td>
    <td align="center" width="20%"><strong>Oxygen</strong><br><sub>about 90 days</sub></td>
    <td align="center" width="20%"><strong>Speed</strong><br><sub>up to x20</sub></td>
    <td align="center" width="20%"><strong>Torpedoes</strong><br><sub>up to x10</sub></td>
    <td align="center" width="20%"><strong>Sonar</strong><br><sub>up to x10</sub></td>
  </tr>
</table>

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
- **Heavy Armor x3**: optional toggle that reduces incoming player submarine and crew damage by x3. It is off by default, and migration settings v16 / v1.4.16 turns old saved Heavy Armor settings off once during migration.
- **DeepDive**: `Plongée x2` is enabled by default in F10. When enabled it doubles depth commands above 10 m: 20->40, 40->80, 150->300, 300->600. Crew depth stress is evaluated on half real depth, so each depth-stress threshold needs roughly twice the real depth. Disabled returns depth orders, pressure damage, stress thresholds, and crush handling to vanilla.
- **Super discrétion / Super Stealth**: optional toggle that divides player noise and detectability by x3. Enemy contacts and detection mechanics still exist.

## Heavy Armor x3

Heavy Armor is a manual F10 mode for players who want a tougher submarine without making it immortal.

<table>
  <tr>
    <td width="33%"><strong>Default</strong><br><sub>Off, so old saves stay predictable after migration.</sub></td>
    <td width="33%"><strong>Protection</strong><br><sub>About one third incoming player submarine damage.</sub></td>
    <td width="33%"><strong>Limits</strong><br><sub>Leaks, fires, repairs, pressure, and fatal crush still matter.</sub></td>
  </tr>
</table>

What is included:

- Reduces player submarine equipment and hull damage to about one third.
- Reduces player crew damage when the damage event explicitly targets the player ship.
- Reduces flaw and fire chances together with player equipment damage.
- Leaves DeepDive crush behavior visible and fatal instead of hiding it behind armor.
- Keeps explosions scoped to the player ship instead of globally weakening every target in a shared blast.
- Keeps the mode disabled by default; migration settings v16 / v1.4.16 turns old saved Heavy Armor settings off once, then preserves later F10 choices.

## DeepDive

DeepDive is enabled by default as `Plongée x2`. It doubles depth orders above 10 m and keeps the pressure model understandable:

| Command | Real target with DeepDive |
| ---: | ---: |
| `20 m` | `40 m` |
| `40 m` | `80 m` |
| `150 m` | `300 m` |
| `300 m` | `600 m` |

Crew depth stress is evaluated on half real depth, so the command profile feels consistent with the displayed x2 mode. Turning the option off returns depth orders, pressure damage, stress thresholds, and crush handling to vanilla.

## Interior Lighting

The runtime lighting patch is enabled by default through `Couleurs eclairage` in F10. It keeps the original UBOAT modes but lets you choose visual colors for Alarm and SilentRun from preset dropdown lists. The shipped defaults stay Alarm amber orange and SilentRun submarine green. Disabled restores vanilla lighting colors.

## Mega Torpedoes

Mega Torpedoes are designed to feel decisive without requiring spreadsheet edits while you test.

<table>
  <tr>
    <td align="center" width="25%"><strong>x10</strong><br><sub>default damage factor</sub></td>
    <td align="center" width="25%"><strong>x3</strong><br><sub>visual blast profile</sub></td>
    <td align="center" width="25%"><strong>0</strong><br><sub>runtime dud chance while active</sub></td>
    <td align="center" width="25%"><strong>F10</strong><br><sub>toggle and slider control</sub></td>
  </tr>
</table>

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
python -m py_compile .\build_uboat_long_submerged_mod.py .\tests\test_long_submerged_generator.py
python -m unittest discover -s tests -v
git diff --check
```

The generated runtime patch is also compiled manually against UBOAT and Unity managed DLLs, including `UnityEngine.UI.dll`, before release.

## Notes

- This is an unofficial mod.
- The mod is designed for **UBOAT 2026.1 Patch 20**.
- Existing saves are supported after a full game restart.
- F10 settings are runtime settings, so players can test values in game before keeping their favorite profile.
