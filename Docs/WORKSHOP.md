# Steam Workshop Description & Upload Guide

This file contains the Steam Workshop metadata, plain-text summary, ready-to-paste Steam BBCode description, recommended tags, and pre-upload verification gate for **Power Grid Manager**.

> **Pre-upload gate:** Do **not** upload to the Steam Workshop until `./build.sh` has built `PowerGridManager.dll` against each advertised RimWorld version (`1.5`, `1.6`, and `1.7`), the in-game checklist in [`Docs/TESTING.md`](TESTING.md) has passed in a clean no-DLC profile, and `./package-release.sh` has produced a verified package. The release packager requires all three DLLs; do not upload until every advertised version has been built and tested. The `--allow-unbuilt` output is a layout dry run, not a Workshop package.

---

## 1. Workshop Metadata

- **Title:** `Power Grid Manager`
- **Package ID:** `ArliT1.PowerGridManager`
- **Mod version:** `1.0.1` (`About/About.xml`)
- **Preview Banner:** `About/Preview.png` (`640×360` PNG — automatically picked up by RimWorld's Workshop uploader via `ModMetaData.GetWorkshopPreviewImagePath()`)
- **Mod List Icon:** `About/ModIcon.png` (`64×64` PNG)
- **Tags:** `Mod` (plus version tags `1.5`, `1.6`, `1.7` automatically populated by RimWorld from `About/About.xml`)
- **Required DLC:** None
- **Required Mods:** None (no Harmony, no HugsLib)

---

## 2. Steam Workshop Description (BBCode — Copy & Paste)

```bbcode
[h1]Power Grid Manager[/h1]

[b]Understand where every watt in your colony goes.[/b] Power Grid Manager adds a native [b]Power[/b] tab to RimWorld with map-wide totals, per-network breakdowns, a searchable building table, recorded in-game history charts, evidence-based diagnostics, battery endurance forecasts, and an optional (disabled-by-default) load-shedding manager.

It observes RimWorld's native electrical simulation rather than replacing or patching it: [b]no DLC required, no Harmony patches, no HugsLib, and zero impact on vanilla power mechanics.[/b]

[h2]Key Features[/h2]
[list]
[*][b]Overview Dashboard:[/b] 24 clear tiles covering measured generation, measured consumption, signed net balance, load ratio, configured generation, battery energy (in watt-days, never confused with watts), storage capacity, charge %, network/component counts, deficit networks, disconnected consumers, active advisories, and battery endurance.
[*][b]Per-Network Inspector:[/b] Inspect every independent power grid on the map with status pills ([i]Stable, High load, Power deficit, Battery charging/discharging, No generation[/i]), stored charge bars, largest active consumers, one-click building filtering, and camera jump.
[*][b]Searchable & Groupable Building Table:[/b] Sort and filter every powered building by current draw, generation, category ([i]Production, Temperature control, Lighting, Research, Medical, Security, Recreation, Generation, Storage[/i]), room (including unassigned buildings), network, flick state, high consumption, or unsupported modded components. Click any row to compare measured vs nominal draw, locate it on the map, or open its vanilla info card.
[*][b]Recorded Power History:[/b] Lightweight, bounded map-scoped charts over the last [i]1 hour, 6 hours, 1 day, 3 days, or 7 days[/i] of in-game time for Generation vs Consumption, Net Balance, Battery Charge %, and Load %. Missing samples are shown honestly as gaps, never fabricated.
[*][b]Evidence-Based Diagnostics:[/b] Actionable advisories for persistent deficits, high load, draining batteries, zero-generation grids, isolated consumers, underperforming generators, and unsupported components — each showing its severity, measured evidence, duration, suggested action, and resolution state, with configurable thresholds, muting, and rate-limited alerts.
[*][b]Honest Battery Endurance Forecast:[/b] Estimates time to battery depletion or full charge from current and historical net balance, accounting for vanilla's 5 W/battery self-discharge, charging efficiency, and EMP-stunned batteries, paired with an explicit confidence rating ([i]High / Medium / Low[/i]).
[*][b]Optional Power Management (Off by Default):[/b] Enable manual On/Off controls for compatible flickable buildings, organise buildings into custom monitoring groups, exclude specific buildings from diagnostics or control, or configure an opt-in hysteresis load-shedding policy with a read-only preview of affected buildings and an auditable event log. Critical infrastructure is protected by default — only buildings you explicitly mark [i]Sheddable[/i] can ever be switched off automatically.
[/list]

[h2]Honest Numbers[/h2]
Every figure is explicitly labelled as [b]measured[/b] (live active output), [b]configured[/b] (building definition rating), or [b]estimated[/b] (projected from current/recorded conditions). Unrecognised custom power components from other mods are flagged cleanly rather than guessed at.

[h2]Performance & Save Safety[/h2]
[list]
[*]Never scans buildings every tick or on UI draw frames (uses a cached periodic snapshot, default every 120 ticks, staggered across multiple maps).
[*]Bounded history and event storage (~243 primitive samples per map at default settings).
[*]Safe to add to existing saves. To remove cleanly while using optional load shedding, click [b]Restore now[/b] or disable automation before saving and removing the mod.
[/list]

[h2]Compatibility[/h2]
[list]
[*][b]No DLC required.[/b] Compatible with all official expansions.
[*][b]No Harmony patches.[/b] Uses public RimWorld APIs only, minimizing mod conflicts.
[*]Works alongside read-only HUD mods such as [i]More Power Info[/i]. If using automatic bench/light switchers such as [i]LightsOut[/i], avoid marking those same buildings as [i]Sheddable[/i] in Power Grid Manager's optional automation.
[/list]

[b]Source Code & Documentation:[/b] https://github.com/ArliT1-F/Power-Grid-Manager
```

---

## 3. Packaging & Publishing Workflow

1. For an **existing Workshop item**, preserve its real `About/PublishedFileId.txt` from the local mod folder used for the earlier upload. Put a copy in this repository's `About/` folder before packaging; the packager copies it into the staged folder and archive when present. If it is missing, recover or verify the actual item ID before proceeding—never guess or create a new item by accident. Check that the ID belongs to the item you intend to update.
2. Rebuild each advertised RimWorld version against its own official installation:
   ```sh
   RIMWORLD_MANAGED_DIR="/path/to/RimWorld-1.5/RimWorldLinux_Data/Managed" RIMWORLD_GAME_VERSION=1.5 ./build.sh
   RIMWORLD_MANAGED_DIR="/path/to/RimWorld-1.6/RimWorldLinux_Data/Managed" RIMWORLD_GAME_VERSION=1.6 ./build.sh
   RIMWORLD_DIR="/path/to/RimWorld-1.7" RIMWORLD_GAME_VERSION=1.7 ./build.sh
   ```
3. Run the static checks and logic test suite:
   ```sh
   python3 Tests/static_checks.py
   python3 Tests/build_version_test.py
   python3 Tests/package_release_test.py
   dotnet run --project Tests/PowerGridManager.Tests.csproj --configuration Release
   ```
4. Assemble the clean distributable folder and archive:
   ```sh
   ./package-release.sh
   # dist/PowerGridManager/ and dist/PowerGridManager-1.0.1.zip
   ```
   For an update, verify `dist/PowerGridManager/About/PublishedFileId.txt` contains the correct existing ID. If the ID was copied in after packaging, it is not in the already-created zip; repackage if the archive must include it.
5. Back up any existing local mod folder, then copy the staged `PowerGridManager` folder into `<RimWorld>/Mods/PowerGridManager`, keeping the ID file. Restart RimWorld so it reads the updated files and ID, run the in-game checklist in [`Docs/TESTING.md`](TESTING.md), and capture live screenshots if needed (see [`Docs/SCREENSHOTS.md`](SCREENSHOTS.md)).
6. **Before invoking the uploader**, check again that the copied folder's ID matches your existing Workshop listing: RimWorld may submit immediately, and a missing or invalid ID can create a new item. Then use the Workshop upload/update action for the **copied local mod folder** in RimWorld's Mods menu. The staged folder excludes `Source/`, `Tests/`, `Docs/`, and `.git/`.
