# AnyRadianceLoader
Switch between the three AnyRadiance versions (1.0, 2.0, 3.0) from the mod menu instead of swapping mod files by hand.

## How to use
1. Open the mod menu and go to **Any Radiance Loader**.
2. Set **AnyRadiance version** to Off, 1.0, 2.0 or 3.0.
3. Restart the game. The chosen version loads at startup like any other mod.

**Loaded now** shows the version the game loaded at startup. If it shows a conflict, two AnyRadiance versions are installed at once (see below).

## Why a restart is needed
All three AnyRadiance builds have the same assembly name and version, so only one of them can be loaded in a game session. The loader therefore does not load anything at runtime. It only puts the chosen version into the Mods folder, and the Modding API loads it normally at the next start. This also keeps ReplayLogger logs clean: AnyRadiance shows up as a normal mod, not as an unaccounted assembly.

## What the loader changes in the Mods folder
- The chosen version goes into the folder ReplayLogger expects: `AnyRadianceFixedDDark` (1.0), `AnyRadiance2-1.5` (2.0) or `AnyRadiance 3.0` (3.0).
- 1.0 and 2.0 need ModCommon. The loader installs it into `ModCommon` if it is missing and never removes it.
- 3.0 needs Vasi. Install Vasi with Lumafly.
- The loader marks the folders it creates and only ever removes those. Folders you installed yourself are left alone; the loader warns in the log if one of them would load together with the chosen version.
- Windows keeps the loaded version's files locked while the game runs. When you switch away from it, the loader removes its folder right after the game closes.

## Credits
- AnyRadiance 1.0: build without the Dream Dark glitch by EarlyHemisphere (https://github.com/EarlyHemisphere/HK-AnyRad-No-Ddark-Glitch).
- AnyRadiance 2.0: original by bob fred, Hollow Knight 1.5 port by EarlyHemisphere (https://github.com/EarlyHemisphere/HollowKnight.AnyRadiance2-1.5).
- AnyRadiance 3.0: jngo102 (https://github.com/jngo102/AnyRadiance).
- ModCommon: HK modding community (https://github.com/HK-Modding-Preservation/ModCommon).
