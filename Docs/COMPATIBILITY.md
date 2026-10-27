# Supported Game Versions & Compatibility

This document records the exact verification status of each targeted RimWorld version, DLC independence, third-party mod compatibility rules, coexistence with established power mods, and known limitations.

---

## 1. Supported RimWorld Versions

`About/About.xml` and `LoadFolders.xml` configure separate version-scoped assembly directories (`1.5/Assemblies`, `1.6/Assemblies`, `1.7/Assemblies`) built via `./build.sh`.

| RimWorld Version | Target Runtime | Build Input | API Verification Status | In-Game Status |
|---|---|---|---|---|
| **RimWorld 1.6** (Primary target) | `.NET Framework 4.7.2` (Mono) | `<RimWorld 1.6>/*_Data/Managed` | **Verified against public 1.6 decompilation** (`Dyyrlysh/RimworldDecompile`, commit `79ca2aa…`) across all Phases 1–9 APIs (`CompPower*`, `PowerNet*`, `CompFlickable`, `FlickUtility`, `Command_Toggle`, `MainTabWindow`, `MapComponent`, `Scribe_*`). Phases 1–6 were also user-compiled and launched in-game. | **Awaiting final Phase 7–9 official-assembly build & in-game sign-off** (see [`Docs/TESTING.md`](TESTING.md)). |
| **RimWorld 1.5** | `.NET Framework 4.7.2` (Mono) | `<RimWorld 1.5>/*_Data/Managed` | **Verified against public 1.5.4409 decompilation** (`kasirii/RimWorld1.5.4409Decompiled`); all referenced types and signatures match 1.6. Compiled separately so assembly version tokens match 1.5. | **Awaiting official 1.5 assembly build & in-game sign-off.** |
| **RimWorld 1.7** | `.NET 10.0` (Waylith) | `<RimWorld 1.7 install root>` (`Assembly-CSharp.dll`, `UnityStubs.dll`, `Waylith.dll`, `Waylith.UnityCompat.dll`) | **Configured, API-unverified.** Build layout follows public multi-version mod conventions (*PrepareLanding*, *More Power Info*), but exact 1.7 power/UI signatures could not be inspected against official 1.7 assemblies in this environment. | **Experimental / untested** until built and launched against an official 1.7 installation. |

### Strict Per-Version Assembly Routing (`LoadFolders.xml`)

`LoadFolders.xml` explicitly maps `<v1.5>`, `<v1.6>`, and `<v1.7>` to `/` plus their respective version directory (`1.5`, `1.6`, `1.7`). This prevents RimWorld 1.6 (Mono / `net472`) from ever falling back to `1.7/Assemblies/PowerGridManager.dll` (`net10.0`) when only a subset of versions has been built locally.

---

## 2. DLC & Dependency Requirements

- **DLC required:** **None.** Works with base-game RimWorld (`Core` only) as well as any combination of *Royalty*, *Ideology*, *Biotech*, *Anomaly*, or *Odyssey*. Static checks enforce zero references to DLC-specific namespaces.
- **External mod dependencies:** **None.** Does not require *Harmony*, *HugsLib*, or any library mod.
- **Load order:** Safe anywhere after `Core`. No XML patches or C# method patches are applied.

---

## 3. Modded Power & Flick Components

Power Grid Manager classifies every building along two independent compatibility axes (`CompatibilityRegistry`):

### 3.1 Monitoring Support (`PowerSupportLevel`)

| Level | Condition | Behaviour |
|---|---|---|
| `Supported` | Vanilla `CompPowerTrader`, `CompPowerPlant`, `CompPowerBattery`, or connection-only `CompPowerTransmitter` | Full measured output, storage, nominal draw, diagnostics, and forecasting. |
| `CustomImplementation` | Subclass of `CompPowerTrader` or `CompPowerBattery` defined outside `Assembly-CSharp` / vanilla namespaces | Standard `PowerOutput` / `StoredEnergy` contract is read and included in totals; building detail card explicitly labels the component as **Modded components** rather than assuming vanilla internals. |
| `Pending` | Component's `PowerNet` reference and `PowerNetManager` registration temporarily disagree during a topology update | Excluded from active network watt totals for that refresh; surfaced as a pending-membership data notice rather than a false disconnection. |
| `Unsupported` | Custom `CompPower` subclass that is neither a trader, battery, nor standard transmitter | Counted in **Unsupported components**, surfaced as an informational notice, and **never** contributes guessed wattage or storage to any total. |

### 3.2 Control Support (`PowerControlSupport` & `BuildingPowerControl`)

Optional switching (`Management` tab and detail-card On/Off buttons) applies stricter guards than monitoring:
- Refused if the building is despawned, non-colonist, user-excluded, has unconfirmed network membership, or carries **any** unsupported `CompPower` component.
- Refused if `CompFlickable.WantsFlick()` is `true` (a player-ordered colonist flick is already pending — the mod never fights player designations).
- Refused if `CompFlickable.CompGetGizmosExtra()` yields **zero** or **more than one** `Command_Toggle` gizmo with non-null `isActive` and `toggleAction` delegates. Modded multi-mode switches are left untouched rather than toggled ambiguously.
- Any exception thrown by a third-party gizmo is caught, recorded in the map's management event log, and logged via `Log.Warning` at most once per component `Type` (hard-capped at 8 types per session so broken mods cannot spam `Player.log`).

---

## 4. Coexistence with Other Power Mods

| Mod | Overlap | Compatibility & Coexistence Notes |
|---|---|---|
| **More Power Info** | Aggregate power bar & stored-energy autonomy | Safe alongside Power Grid Manager (both are read-only observers). Autonomy figures will differ slightly when batteries hold charge because Power Grid Manager subtracts vanilla's `5 W`/battery self-discharge by default (`ForecastIncludeSelfDischarge`). |
| **LightsOut** | Automatic standby/off switching for lights and benches when not in use | Safe when Power Grid Manager's **Management** tab is left at its default (`OFF`), or for monitoring. Avoid marking *LightsOut*-managed benches or lights as **Sheddable** under Power Grid Manager's load-shedding policy, as both mods would attempt to manage their switch state. |
| **Turn It On and Off — RePowered** | Dynamic idle vs active `PowerOutput` scaling | Monitoring reflects the dynamic `PowerOutput` reported on each refresh (`60–600` ticks). Because per-building history is not recorded, the building detail card's daily energy figure is explicitly marked as an instantaneous **estimate**. |
| **RT Power Switch / PowerControl** | Grid-splitting switches or custom demand controllers | Splitting or merging conduits changes `PowerNet` instances cleanly on the next refresh; aggregate map history remains continuous. Custom switch components without a single standard `CompFlickable` toggle gizmo are marked unsupported for remote switching. |
| **Mods with completely separate energy frameworks** (e.g. pipe/mana/heat networks not inheriting `CompPower`) | Separate non-electrical resource networks | Invisible to `PowerGridScanner` by design; only `CompPower`-based electrical infrastructure is scanned. |

---

## 5. Known Compatibility & Architectural Limitations

1. **No official game binaries in the build sandbox:** Source, XML, localization, and 1,700+ C# logic checks pass in `.NET 8` CI against verified test doubles, and APIs were cross-checked against public 1.5 and 1.6 decompilations, but final compilation against official 1.5/1.6/1.7 game DLLs and in-game testing must be performed on a machine with RimWorld installed.
2. **RimWorld 1.7 runtime unverified:** `1.7` targets `net10.0` + Waylith stubs; treat 1.7 support as experimental until compiled and tested against an official 1.7 build.
3. **No fabricated "installed generation capacity":** Vanilla and modded `CompPowerPlant` subclasses compute output procedurally (wind, solar, geothermal, watermill, fuel, toxifier, modded reactors). `Props.PowerConsumption` is shown only as qualified **Configured generation** and is never treated as guaranteed capacity.
4. **Map-wide history only (no per-building or per-network time series):** RimWorld `PowerNet` instances have no persistent identity across splits, merges, or reloads, and per-building history on large colonies would multiply save size and tick cost. Per-building daily energy is therefore labelled as an instantaneous estimate, and network advisories quote historical averages only when the map has a single network.
5. **No invented per-battery discharge ceiling:** Because no public per-battery watt discharge cap is confirmed across target versions, large-deficit depletion forecasts divide usable energy directly by net loss (a conservative/shorter depletion time if a modded battery caps discharge rate).
6. **User-assigned criticality:** `PowerCategory` (`Temperature`, `Medical`, `Production`, etc.) is used strictly for table grouping and filtering, never to guess whether a building is safe to turn off. Every building is protected until the player explicitly marks it **Sheddable**.
7. **Rapidly cycling consumers and zero-draw buildings:** Load shedding acts on measured draw at each refresh (`60–600` ticks). Opted-in buildings currently drawing `0 W` are skipped during shedding because switching them off would save `0 W` of the active shortfall.
8. **Vanilla flick concept tutorial on brand-new profiles:** Calling `FlickUtility.UpdateFlickDesignation` invokes vanilla `TutorUtility.DoModalDialogIfNotKnown(ConceptDefOf.SwitchFlickingDesignation)` once if the Learning Helper is active and the player has never flicked a switch before.
9. **Mod removal while buildings are auto-shed:** Because switch state lives on vanilla `CompFlickable`, removing the mod while load shedding actively holds buildings off leaves those switches off until flicked back on in vanilla (clicking **Disable** or **Restore now** before removing the mod restores them automatically).
10. **Replaced buildings receive new IDs:** Preferences and tracking use `Thing.GetUniqueLoadID()`. If a building is destroyed and rebuilt, the new instance receives a fresh ID and defaults back to protected.
