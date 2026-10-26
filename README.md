# Power Grid Manager

A RimWorld mod focused on readable colony power monitoring. It observes RimWorld's native
power simulation; it does not replace or patch it.

> **Development status:** Phases 1–8 are implemented in source (project skeleton, monitoring
> core, dashboard, historical monitoring, diagnostics, forecasting, optional management, and
> compatibility/optimization work). The offline logic harness and the dependency-free static
> checker both pass in GitHub Actions CI against .NET 8 (the harness links the production
> sources against minimal game doubles — it is *not* a build against the real RimWorld
> assemblies). **No game assemblies or RimWorld installation are available here**, so the
> production build (`build.sh`) and every in-game behaviour remain unverified. Treat this as a
> development build, not a release, until the [in-game checklist](Docs/TESTING.md) is run.

## What it does today

Open the **Power** tab to get a dashboard over the map's electrical infrastructure:

- **Overview** — 24 tiles covering measured generation, measured consumption, net balance,
  consumption/generation ratio, configured (def-level) generation, stored energy, storage
  capacity, charge, network/component counts, deficit and high-load networks, disconnected
  consumers, data notices, nominal draw, unsupported components, switched-off consumers and
  the history cadence. A panel underneath states exactly what those numbers include.
- **Networks** — every simulated power network with its own generation, consumption,
  balance, counts, battery energy and charge, a status label (stable, high load, deficit,
  battery charging/discharging, no generation, unknown), its largest consumers, and buttons
  to filter the building table or jump to a real building on that network.
- **Buildings** — searchable, sortable, groupable table of every powered building with
  measured draw, output, network, status and flick state; filters for role, network,
  category, flickable, switched-off, high consumption, data notices and unknown behaviour;
  a detail card with nominal vs measured draw, trader state, room, position, an explicitly
  labelled daily-energy *estimate*, and locate/inspect buttons.
- **History** — in-game-time charts (hour, 6 hours, day, 3 days, 7 days) of generation vs
  consumption, net balance, battery charge and consumption relative to generation, sampled
  at a configurable interval, bounded by a retention window and stored in the save.
- **Diagnostics** — advisories derived from the readings: power deficits, high load, draining
  batteries, networks with no generation, isolated consumers, underperforming generators,
  draw above the configured amount, switched-off buildings that still draw, and unsupported
  components. Each one carries its severity, a description, the evidence behind it (including
  how long it has been true), and a suggested action. Conditions that clear are reported as
  resolved. Kinds can be muted; individual advisories can be dismissed; new warning and
  critical advisories can raise the game's own on-screen message, rate-limited.
- **Forecast** — battery endurance: how long until the map's stored energy runs out, or until
  it is full, computed from the current balance and — when enough history exists — the
  recorded average. Battery self-discharge (a verified 5 W per battery, which the trader
  balance excludes) and charge efficiency are accounted for. Every estimate states its basis
  and a confidence level (low / medium / high), and estimates beyond 30 in-game days are
  reported as "more than" rather than as a number.

Everything the mod reports is labelled as **measured**, **configured** or **estimated**.
Figures that cannot be verified (installed capacity, per-building history, unsupported
custom power systems) are reported as unavailable with an explanation instead of a guess.

Forecast figures are explicitly labelled as **estimates**: they assume the observed balance
continues, and the panel states what it assumed. They are never written back into the game's
power simulation.

- **Management** — optional, disabled by default. Compatible buildings can be switched on/off
  through the game's public flick API; buildings can be put into user-defined monitoring groups,
  excluded from building-level diagnostics, excluded from control, or explicitly whitelisted as
  protected. A load-shedding policy can be armed with user-configurable charge thresholds, a
  hysteresis band, a stability period, a cooldown, a per-pass action cap, per-building priorities
  (1–6) and staged / all-at-once / manual recovery. A read-only preview lists the buildings a
  policy would affect before it is enabled; a bounded event history records every attempted
  action and refusal. Monitoring works with management off, and only buildings the player
  explicitly marked sheddable can ever be switched off automatically.

The dashboard's "data notices" are observed data-quality facts, not diagnostics. Verified
`PowerCategory` values are grouping helpers, **not** an automatic criticality classifier:
there is no automatic detection that a building is safe to disable.

## Requirements

- RimWorld 1.5, 1.6 or 1.7 (each needs its own binary; see below).
- No DLC required, no HugsLib, no Harmony patches, no external service or account.

## Build

The mod ships no DLLs in this repository: build against your own installation. The script
writes version-scoped binaries to `1.5/Assemblies`, `1.6/Assemblies` or `1.7/Assemblies`
and never downloads or bundles game assemblies.

```sh
# RimWorld 1.5 / 1.6 (bundled Mono, net472) — version is read from Version.txt
RIMWORLD_MANAGED_DIR="$HOME/.steam/steam/steamapps/common/RimWorld/RimWorldLinux_Data/Managed" ./build.sh

# Windows equivalent
# set RIMWORLD_MANAGED_DIR=C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed

# RimWorld 1.7 (.NET 10 / Waylith) — pass the installation root instead
RIMWORLD_DIR="/path/to/RimWorld-1.7" ./build.sh
```

- The version is detected from `Version.txt` next to the `Managed` directory; set
  `RIMWORLD_GAME_VERSION=1.5` (or `1.6`/`1.7`) to override it.
- 1.5/1.6 need `Assembly-CSharp.dll`, `UnityEngine.CoreModule.dll`,
  `UnityEngine.IMGUIModule.dll` and `UnityEngine.TextRenderingModule.dll` from that install;
  the script verifies them first.
- The build uses `dotnet` or `msbuild`. On Linux the .NET SDK restores the .NET Framework
  4.7.2 reference assemblies from NuGet, so the first build needs network access. If no
  `dotnet`/`msbuild` is present, the script falls back to `mcs`/`csc` against the game's own
  assemblies (Mono: `sudo apt install mono-devel`).
- Output: `1.6/Assemblies/PowerGridManager.dll` (adjust the folder for your version). It is
  git-ignored — never commit or redistribute game DLLs.

## Try it in game

Link the repository into the game's `Mods` folder so a rebuild takes effect immediately:

```sh
ln -s "/path/to/Power-Grid-Manager" "$HOME/.steam/steam/steamapps/common/RimWorld/Mods/PowerGridManager"
```

Then enable **Power Grid Manager** in the mod list, restart, and open the **Power** tab.
If the tab is missing or nothing happens, check the log for load errors:

```sh
tail -n 80 "$HOME/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log"
```

## Test

```sh
dotnet run --project Tests/PowerGridManager.Tests.csproj --configuration Release   # .NET 8 SDK
python3 Tests/static_checks.py                                                     # no dependencies
python3 Tests/static_checks.py --syntax                                             # optional Roslyn parse
```

The C# harness links the production sources against minimal game doubles and runs the
monitoring, dashboard, history, diagnostics, forecast, management, compatibility and
localization checks. It prints the exact total; it proves logic only, not game-binary
compatibility. `--syntax` needs Node.js 22+ and `npm i @scalar/csharp-fmt`; set
`PGM_CSHARP_FMT` if the package is installed elsewhere. CI runs the static checker and the
harness on every push/PR.

The local workspace has no `dotnet`, but the `Offline logic tests` GitHub Actions job compiles
the full production source set against the doubles and runs the harness on .NET 8; that job
passes for this branch. Locally `python3 Tests/static_checks.py --syntax` also passes — 1,853
static assertions including XML metadata, 568 localization keys/placeholders, source hygiene
and a Roslyn syntax parse of all 50 C# files, with zero failures. See
[Phase 8](Docs/PHASE-8-COMPATIBILITY.md) for the full verification matrix and
[Testing](Docs/TESTING.md) for the in-game checklist.

## Install (once built)

Copy this folder (or a release folder containing `About/`, `Defs/`, `Languages/`,
`Textures/` and the matching `1.X/Assemblies/`) into RimWorld's `Mods` directory, enable
**Power Grid Manager**, and restart.

## Documentation

- [Technical research, version matrix and implementation plan](Docs/TECHNICAL-RESEARCH.md)
- [Phase 2: monitoring core, measurement contracts](Docs/PHASE-2-MONITORING.md)
- [Phase 3: dashboard, UI API verification, layout](Docs/PHASE-3-DASHBOARD.md)
- [Phase 4: historical monitoring, retention, save/load](Docs/PHASE-4-HISTORY.md)
- [Phase 5: diagnostics, advisory kinds, alert throttling](Docs/PHASE-5-DIAGNOSTICS.md)
- [Phase 6: battery endurance forecasting, confidence](Docs/PHASE-6-FORECAST.md)
- [Phase 7: optional management, control APIs, load shedding](Docs/PHASE-7-MANAGEMENT.md)
- [Phase 8: compatibility, optimization, verified limitations](Docs/PHASE-8-COMPATIBILITY.md)
- [Testing: what the harness proves and the in-game checklist](Docs/TESTING.md)
- [Changelog](CHANGELOG.md)
