# Power Grid Manager

![Power Grid Manager Banner](About/Preview.png)

**Power Grid Manager** is a RimWorld mod that gives players a centralized, native dashboard for monitoring, diagnosing, forecasting, and optionally managing their colony's electrical infrastructure. It observes RimWorld's built-in electrical simulation (`PowerNetManager`, `PowerNet`, `CompPowerTrader`, `CompPowerBattery`, `CompFlickable`) and never replaces, patches, or alters vanilla power mechanics.

- **No DLC required** — works with base-game RimWorld (`Core` only) and with any expansion.
- **No external dependencies** — no Harmony patches, no HugsLib, no reflection, no background threads, and no network services.
- **Honest numbers** — every value is explicitly distinguished as **measured**, **configured**, or **estimated**, and power (**W** / **kW**) is never confused with stored energy (**Wd**).

> **Release status (`0.9.0-rc.1`):** All nine development phases are implemented in source, packaged with `LoadFolders.xml`, `LICENSE`, `About/Preview.png`, Workshop metadata, and `package-release.sh`, and verified by both `Tests/static_checks.py` (**1,929 static checks**, **1,931** with `--syntax`) and the `.NET 8` CI test harness (**1,744 automated C# checks** against game doubles). Because no RimWorld installation or proprietary game DLLs are present in the automated build environment, final compilation via `./build.sh` against official RimWorld `1.5` / `1.6` / `1.7` assemblies and execution of the [in-game QA checklist](Docs/TESTING.md) remain required before promoting `0.9.0-rc.1` to a signed-off `1.0.0` release.

---

## Features

Open the **Power** tab on the main bottom bar to access seven integrated views:

| Tab | What It Provides |
|---|---|
| **Overview** | 24 metric tiles covering measured generation, measured consumption, signed net balance, load ratio, configured def-level generation, physical stored battery energy (`Wd`), storage capacity (`Wd`), battery charge (`%`), network & component counts, deficit & high-load network counts, disconnected consumers, data notices, active & critical advisories, battery endurance, forecast confidence, nominal draw, unsupported components, switched-off consumers, and history cadence — plus an inline explanation of every aggregate. |
| **Networks** | Lists every independent `PowerNet` with net balance, operational status pill (*Stable*, *High load*, *Power deficit*, *Battery charging*, *Battery discharging*, *No generation*, *Unknown*), consumer/generator/battery counts, charge bar, endurance estimate, largest active consumers, one-click building filtering, and **Locate** camera jump. |
| **Buildings** | Searchable, stably sorted, groupable table of every powered building. Filter by role, network, conservative category (*Production*, *Temperature control*, *Lighting*, *Research*, *Medical*, *Security*, *Recreation*, *Generation*, *Storage*, *Uncategorized*), room (including *"No room assigned"*), flickable, switched-off, high consumption, data notices, unknown behaviour, or colonist ownership. Select any row for measured vs nominal draw, trader state, coordinates, estimated daily energy (`Wd`), **Locate**, and **Inspect**. |
| **History** | Map-scoped, save-persisted charts over the last **1 hour**, **6 hours**, **1 day**, **3 days**, or **7 days** of in-game time for *Generation vs Consumption*, *Net Balance*, *Battery Charge %*, and *Load %*. Missing sample buckets render honestly as gaps, never interpolated values. |
| **Diagnostics** | Ten evidence-based advisory kinds (*Persistent deficit*, *High load*, *Battery discharge*, *No generation*, *Isolated consumer*, *Pending membership*, *Underperforming generator*, *Draw above configured*, *Switched off but drawing*, *Unsupported component*) with severity pills, measured evidence, duration, suggested action, resolved-episode retention, per-kind muting, per-advisory dismissal, and rate-limited on-screen alerts. |
| **Forecast** | Estimates time to battery depletion or full charge from current and recorded historical balance, accounting for vanilla's `5 W`/battery continuous self-discharge, charging `Props.efficiency`, and EMP-stunned batteries. Displays explicit confidence (*High* / *Medium* / *Low*), current-vs-historical comparison, per-network endurance, and stated assumptions. |
| **Management** *(Off by default)* | Optional manual On/Off switching for compatible `CompFlickable` buildings, up to 16 user-defined monitoring groups, per-building diagnostic/control exclusions, user-assigned priorities (`1–6`), and an opt-in hysteresis load-shedding policy with a read-only **"Buildings this policy would affect"** preview, staged/all/manual recovery, and a bounded 40-entry audit log. Critical buildings are protected by default — only buildings explicitly marked *Sheddable* can ever be switched off automatically. |

---

## UI Layout Previews

> *Disclosure:* The previews below are rendered offline from the mod's C# panel layout geometry, `PowerUiDraw` colours, and English `Keyed` strings rather than a live RimWorld client. See [`Docs/SCREENSHOTS.md`](Docs/SCREENSHOTS.md) for full details and the in-game capture guide.

| Overview Dashboard | Networks & Buildings Explorer |
|---|---|
| ![Overview Preview](Docs/Screenshots/01-overview-dashboard.png) | ![Networks & Buildings Preview](Docs/Screenshots/02-networks-and-buildings.png) |

| Recorded History & Endurance Forecast | Evidence-Based Diagnostics & Optional Management |
|---|---|
| ![History & Forecast Preview](Docs/Screenshots/03-history-and-forecast.png) | ![Diagnostics & Management Preview](Docs/Screenshots/04-diagnostics-and-management.png) |

---

## Supported Game Versions

Each RimWorld version compiles into its own isolated `1.X/Assemblies/PowerGridManager.dll` folder, routed strictly via `LoadFolders.xml`:

| RimWorld Version | Runtime Target | Verification Status |
|---|---|---|
| **1.6** *(Primary)* | `.NET Framework 4.7.2` (Mono) | All APIs verified against public 1.6 decompilation; Phases 1–6 user-compiled in-game; awaiting final Phase 7–9 official-assembly build & QA checklist. |
| **1.5** | `.NET Framework 4.7.2` (Mono) | All APIs verified against public 1.5.4409 decompilation; awaiting official 1.5 assembly build & QA checklist. |
| **1.7** | `.NET 10.0` (Waylith) | Build target configured; APIs unverified against official 1.7 assemblies (**experimental / untested** until built on 1.7). |

See [`Docs/COMPATIBILITY.md`](Docs/COMPATIBILITY.md) for the full version and mod-compatibility matrix.

---

## Installation

### From a Built Release Archive (`.zip`)

1. Download `PowerGridManager-<version>.zip` from a verified release (or build one locally with `./build.sh` and `./package-release.sh` below).
2. Extract the `PowerGridManager/` folder into your RimWorld `Mods/` directory:
   - **Linux:** `~/.steam/steam/steamapps/common/RimWorld/Mods/PowerGridManager`
   - **Windows:** `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\PowerGridManager`
   - **macOS:** `~/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods/PowerGridManager`
3. Launch RimWorld, enable **Power Grid Manager** in **Mods** (no DLC or library mod needed), and restart when prompted.
4. Load or start a colony and click the **Power** button on the bottom main-tab bar. Adjust refresh intervals, thresholds, or alert preferences anytime under **Options → Mod Settings → Power Grid Manager**.

---

## Reproducible Build & Release Packaging

This repository intentionally commits **no** proprietary game assemblies or prebuilt `.dll` binaries. Build against your own RimWorld installation using `./build.sh`:

### 1. Compile `PowerGridManager.dll`

```sh
# RimWorld 1.5 or 1.6 (bundled Mono, net472) — version is auto-detected from Version.txt
RIMWORLD_MANAGED_DIR="$HOME/.steam/steam/steamapps/common/RimWorld/RimWorldLinux_Data/Managed" ./build.sh

# Explicit version override when building multiple installs side-by-side:
RIMWORLD_MANAGED_DIR="/path/to/RimWorld-1.5/RimWorldLinux_Data/Managed" RIMWORLD_GAME_VERSION=1.5 ./build.sh
RIMWORLD_MANAGED_DIR="/path/to/RimWorld-1.6/RimWorldLinux_Data/Managed" RIMWORLD_GAME_VERSION=1.6 ./build.sh

# RimWorld 1.7 (.NET 10 / Waylith) — pass the installation root
RIMWORLD_DIR="/path/to/RimWorld-1.7" RIMWORLD_GAME_VERSION=1.7 ./build.sh
```

- **Windows PowerShell equivalent (1.5 / 1.6):**
  ```powershell
  dotnet build Source/PowerGridManager/PowerGridManager.csproj -c Release `
    -p:RimWorldGameVersion=1.6 `
    -p:RimWorldManagedDir="C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed"
  ```
- **Compiler requirements:** `.NET SDK` (`dotnet`, recommended — restores `net472` reference assemblies automatically on first build), `msbuild`, or Mono `mcs`/`csc`.
- **Outputs:** `1.5/Assemblies/PowerGridManager.dll`, `1.6/Assemblies/PowerGridManager.dll`, or `1.7/Assemblies/PowerGridManager.dll`.

### 2. Package a Clean Distributable Release (`dist/`)

After building all three advertised version-scoped DLLs, assemble a clean player/Workshop folder (excluding `Source/`, `Tests/`, `Docs/`, `.git/`, and `.pdb` files):

```sh
./package-release.sh
# Produces:
#   dist/PowerGridManager/
#   dist/PowerGridManager-0.9.0-rc.1.zip
```

For development iteration, you can also symlink the repository directly into `Mods/` after running `./build.sh`:

```sh
ln -s "$(pwd)" "$HOME/.steam/steam/steamapps/common/RimWorld/Mods/PowerGridManager"
```

---

## Testing & Verification

```sh
# 1. Dependency-free static checks (XML metadata, LoadFolders, 568 keyed strings, source hygiene, doc links)
python3 Tests/static_checks.py

# 2. Optional Roslyn C# syntax check across all source & test files (requires Node 22+ and @scalar/csharp-fmt)
python3 Tests/static_checks.py --syntax

# 3. Full C# offline logic & UI smoke harness (.NET 8 SDK; runs automatically on every push/PR in GitHub Actions)
dotnet run --project Tests/PowerGridManager.Tests.csproj --configuration Release

# 4. Dry-run release packager layout check (when no game DLLs are present locally)
./package-release.sh --allow-unbuilt
```

- **What automated CI proves:** All 37 production C# files compile cleanly against minimal game doubles and pass **1,744 checks** covering scanner math, `PowerOn` gating, EMP batteries, 10,000-building maps, multi-map staggering, sorting/filtering/grouping, bounded history & Scribe corruption recovery, diagnostic episodes & alert throttling, endurance forecasting, optional load shedding & hysteresis, localization completeness, and release metadata.
- **What requires a RimWorld installation:** Compiling against official game assemblies, in-game UI rendering across resolutions/UI scales, live frame-time profiling, and real save/reload sign-off. Follow [`Docs/TESTING.md`](Docs/TESTING.md) step by step.

---

## Known Limitations Summary

- **No fabricated installed capacity:** Generator def ratings are displayed only as muted *Configured generation*, never as guaranteed production.
- **Map-wide history only:** Per-building and per-network historical series are intentionally omitted to keep tick overhead and save size small and because runtime `PowerNet` instances have no persistent identity across splits/merges.
- **User-defined criticality:** Every building defaults to *Protected*. Categories (*Medical*, *Temperature*, etc.) never auto-shed or auto-classify safety.
- **Standard `CompFlickable` control boundary:** Modded switches without a single standard `Command_Toggle` gizmo or with pending colonist flicks are refused cleanly rather than forced via private reflection.
- **Mod removal while auto-shed:** Click **Restore now** or disable automation before removing the mod from a save so buildings switched off by a policy do not remain flicked off in vanilla save data.

---

## Documentation

- [Technical Architecture (`Docs/ARCHITECTURE.md`)](Docs/ARCHITECTURE.md)
- [Supported Versions & Mod Compatibility (`Docs/COMPATIBILITY.md`)](Docs/COMPATIBILITY.md)
- [Implemented Features & Remaining Limitations (`Docs/FEATURES-AND-LIMITATIONS.md`)](Docs/FEATURES-AND-LIMITATIONS.md)
- [UI Previews & Screenshot Capture Guide (`Docs/SCREENSHOTS.md`)](Docs/SCREENSHOTS.md)
- [Steam Workshop Description & Upload Guide (`Docs/WORKSHOP.md`)](Docs/WORKSHOP.md)
- [Testing & In-Game QA Checklist (`Docs/TESTING.md`)](Docs/TESTING.md)
- [Initial Technical Research & API Notes (`Docs/TECHNICAL-RESEARCH.md`)](Docs/TECHNICAL-RESEARCH.md)
- Phase design records: [Phase 2](Docs/PHASE-2-MONITORING.md) · [Phase 3](Docs/PHASE-3-DASHBOARD.md) · [Phase 4](Docs/PHASE-4-HISTORY.md) · [Phase 5](Docs/PHASE-5-DIAGNOSTICS.md) · [Phase 6](Docs/PHASE-6-FORECAST.md) · [Phase 7](Docs/PHASE-7-MANAGEMENT.md) · [Phase 8](Docs/PHASE-8-COMPATIBILITY.md)
- [Changelog (`CHANGELOG.md`)](CHANGELOG.md)

---

## License

Released under the [MIT License](LICENSE).
