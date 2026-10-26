# Phase 7 — Optional management

Updated 2026-10-26. This document describes the implemented source and the evidence behind it.
It is **not** a claim of in-game validation: no RimWorld installation, game assemblies or .NET
SDK are available in this workspace. Compilation against real assemblies, launching the game and
the in-game checklist at the end remain required.

## Scope and safety decisions

Phase 7 adds an **optional, opt-in** control layer on top of the read-only monitoring built in
Phases 1–6. The non-negotiable rules are:

1. `ManagementEnabled` and `LoadSheddingEnabled` are both **false by default** and are persisted
   as mod settings. Monitoring, history, diagnostics and forecasting never read them.
2. Vanilla power simulation is never replaced, patched or ticked. There is no Harmony patch, no
   reflection, no background thread and no change to `CompPowerTrader.PowerOutput` or
   `PowerNet`. The only write is a building's flick switch, through the game's own public API.
3. A building is **protected by default**. Automatic shedding only considers buildings the
   player explicitly marked as sheddable. Generators, batteries, unsupported components,
   unconfirmed network membership, non-colonist buildings and user-excluded buildings are
   never candidates.
4. The Management tab shows a **read-only preview** of the buildings a policy would affect,
   including the total measured draw, before automation is enabled. The preview is produced by
   the same evaluator as the runtime policy, so it cannot drift from reality.
5. Every attempted change is recorded in a bounded, map-scoped event history with the outcome
   and a short reason. Refusals are visible; they are not swallowed.
6. Hysteresis, a stability window, a cooldown and a per-pass action cap prevent rapid toggling.
   Recovery is configurable (staged / all at once / manual).

## API research (public references, not installed binaries)

The control path was checked against both public decompilation references used by earlier
phases: the 1.5.4409 decompilation and the 1.6 reference at commit
`79ca2aa15c9e116bd43d448692b62636467f6c46`. Files inspected:
`RimWorld/CompFlickable.cs`, `RimWorld/FlickUtility.cs`, `RimWorld/JobDriver_Flick.cs`,
`RimWorld/WorkGiver_Flick.cs`, `Verse/Command_Toggle.cs`, `Verse/ThingComp.cs`,
`Verse/Thing.cs`, `Verse/Map.cs`, `Verse/Scribe_Deep.cs`, `Verse/Scribe_Values.cs`,
`Verse/ParseHelper.cs`.

Verified members (identical in the 1.5 and 1.6 references):

| Member | Why it matters |
|---|---|
| `CompFlickable.SwitchIsOn` (public get/set) | Setter broadcasts `FlickedOn`/`FlickedOff` and dirties the map mesh; the supported way to apply a switch state. |
| `CompFlickable.WantsFlick()` | Reports a pending player-requested flick. Control is refused while one is pending. |
| `CompFlickable.DoFlick()` | Toggles the switch and plays a sound. Not used for batches (avoids stacking sounds). |
| `ThingComp.CompGetGizmosExtra()` | Public virtual; vanilla `CompFlickable` yields one `Command_Toggle` for player-faction buildings. |
| `Command_Toggle.isActive` / `.toggleAction` | Public delegates. `toggleAction` changes the **private** desired state (`wantSwitchOn`) and calls `FlickUtility.UpdateFlickDesignation`. |
| `FlickUtility.UpdateFlickDesignation(Thing)` | Adds/removes the `Flick` designation based on `WantsFlick()`. |
| `FlickUtility.WantsToBeOn(Thing)` | Reports the effective power state used elsewhere in the game. |
| `Thing.GetUniqueLoadID()` / `Thing.ThingID` | `"Thing_" + defName + thingIDNumber`; the game persists it as the `id` node and uses it for load references. Used as the stable policy key. |
| `Map.uniqueID` | Persisted map ID; used only to stagger first scans across maps. |
| `Scribe_Values.Look<T>` / `Scribe_Collections.Look<T>` / `Scribe_Deep.Look<T>` | Supported serialization. Enums and primitive `List<T>` with `LookMode.Value` are supported by `ParseHelper.FromString`. |

**Serialization detail checked in Phase 8:** `Scribe_Deep.Look` itself acts only in `Saving` and
`LoadingVars`; `PostLoadInit` re-entry of nested objects is performed separately by
`PostLoadInitter` for objects registered during loading. Rather than depend on that registration
detail, Phase 7 classes (and the pre-existing `PowerHistoryTracker`) now clamp and normalise at
the end of `LoadingVars` as well as in `PostLoadInit`. Both passes are idempotent.

### The two-flag problem

`CompFlickable` keeps `switchOnInt` and a **private** `wantSwitchOn`. Setting only
`SwitchIsOn` would leave the gizmo showing the wrong state, and a later
`UpdateFlickDesignation` call could create a `Flick` designation that makes a colonist flick the
building straight back — exactly the oscillation Phase 7 must avoid. Reflection into the private
field would be fragile across 1.5/1.6/1.7, so it is not used.

`Integration/BuildingPowerControl.cs` therefore:

1. refuses if the building is gone, has no `CompFlickable`, or `WantsFlick()` is true;
2. enumerates `CompGetGizmosExtra()` and requires **exactly one** usable `Command_Toggle`
   (a modded component with zero or several toggles is refused, never guessed at);
3. invokes `toggleAction()` if the desired state differs, syncing the private desired state;
4. applies `flick.SwitchIsOn = desired` immediately (same signals, no sound);
5. calls `FlickUtility.UpdateFlickDesignation(building)` to delete any designation just created;
6. verifies both the switch and the gizmo's `isActive()` before reporting success.

An exception from a modded gizmo is contained, recorded in the event log, and logged at most
once per component type (with a hard cap) instead of spamming the log.

## Architecture

```
Core/PowerGridManagerSettings.cs        new persisted policy fields + NormalizeManagement()
Core/PowerGridManagerComponent.cs       one Tick() call after each published snapshot; stagger
Core/PowerGridManagerMod.cs             scrollable settings section for thresholds/recovery
Integration/BuildingPowerControl.cs     compatibility decision + the only switch writer
Integration/CompatibilityRegistry.cs    PowerControlSupport labels, custom-type detection
Integration/PowerManagementRegistry.cs bounded map-scoped preferences + monitoring groups
Analytics/LoadSheddingState.cs          hysteresis state + read-only decision object
Analytics/LoadSheddingEngine.cs         pure evaluation, Apply(), RestoreAll()
Analytics/PowerManagementEventLog.cs    bounded event history
Analytics/PowerManagementController.cs orchestration + cached UI preview
Analytics/PowerDiagnostics.cs           optional per-building exclusion parameter
UI/PowerManagementPanel.cs              new Management tab
UI/BuildingPowerPanel.cs                optional control row on the building detail card
UI/PowerUiState.cs / MainTabWindow...   new tab and transient view state
```

### Persistent identifiers

`PowerManagementRegistry` stores rows keyed by `Thing.GetUniqueLoadID()` — a string the game
itself saves and resolves. Each row also caches the def name, label and cell for display, but
identity never depends on them. Rows are bounded to 2,000; long-missing rows are pruned after
30 in-game days. Loading tolerates missing nodes, truncated parallel lists, duplicate IDs,
out-of-range priorities, unknown enum values and future format versions. Stale group names are
cleared *and* their sheddable flag is removed, so an old save can never enable control through a
group that no longer exists.

`LoadSheddingState` persists the IDs of buildings **this mod** switched off (`autoShed`), the
hysteresis condition and the last action tick. It never restores a building the player switched
off by hand: restore candidates are read from the live `CompFlickable`, and a building already on
is simply untracked. If a tracked building was destroyed or despawned, its ID is untracked and
the event is recorded. Disabling automation (or management) restores everything the policy
switched off, so turning the feature off is not a trap.

Removing the mod from a save leaves ordinary vanilla data intact (preferences live in a
`MapComponent` node), but any building left switched off by a policy at the moment the mod is
removed stays off — flick state is vanilla data. This is a documented limitation.

### Policy evaluation

`LoadSheddingEngine.Evaluate()` is a read-only pass over an existing `PowerGridSnapshot`; it is
called once per refresh (60–600 ticks, default 120), never per tick and never from the draw
pass. It computes:

- usable charge = `AvailableStoredWattDays / CapacityWattDays` (EMP-stunned energy excluded);
- deficit = measured consumption − measured generation (the snapshot's `NetWatts`);
- trigger = charge ≤ low threshold **and** (optionally) a measured deficit;
- recovery = charge ≥ restore threshold, or no usable battery capacity at all;
- a stability window (`LoadShedStableTicks`) and a cooldown (`LoadShedCooldownTicks`).

Three condition kinds are tracked separately so a new batch waits for its own stability window:
first activation, additional shedding while still below recovery, and recovery. That, plus the
hysteresis band between the two thresholds, prevents rapid on/off cycling. At most
`LoadShedMaxActionsPerPass` switches change in a batch, and shedding stops once measured savings
cover the observed deficit. Savings are **measured current draw**, never nominal/configured draw,
and buildings with zero measured draw are not switched off automatically.

Candidates are sorted by the player's per-building priority (1–6, higher = shed first), then by
measured draw, then by scan index for determinism. Restoration is sorted by ascending priority
(most important first). `RecoveryMode.Staged` restores a few at a time, `All` restores all in one
capped batch, and `Manual` leaves restoration to the "Restore now" button.

### Groups and priorities

Groups are user-defined labels for monitoring and for explicit bulk actions (mark/clear
sheddable, apply a priority). A group never enables control by itself and never overrides a
building's exclusion. Priority is a per-building number the player sets with `–`/`+`; the
verified `PowerCategory` from Phase 3 is still displayed for grouping but is **not** used as an
automatic policy rule, because a category alone cannot prove that a building is non-critical
(freezers and medical equipment are both temperature-controlled, for example). This is the
conservative interpretation of "user-configurable priorities".

### UI

- New **Management** tab with an explicit OFF/ARMED/WATCHING/SHEDDING/RESTORING status pill
  (colour is always paired with a word).
- Two clearly separate switches: "Enable controls" and "Enable automation".
- A disabled-by-default note explaining that monitoring is independent.
- Metrics: usable charge, net balance, sheddable count, currently auto-shed count.
- Threshold/recovery presets, a read-only affected-buildings preview and an explanation of the
  current reason (waiting for stability, cooldown, no batteries, no candidates, etc.).
- Group creation/deletion/filtering and bulk actions.
- A paged building table with a selected-building action bar: on/off, group, priority,
  sheddable/protected, exclude from control, exclude from diagnostics, locate.
- Event history with time, kind, building, success/refusal and detail tooltip.
- The building detail card gains the same on/off, group and diagnostic-exclusion controls when
  management is enabled. It keeps the existing Locate/Inspect/Clear buttons.

The whole tab is one scroll view with fixed-height rows; no nested scroll views and no per-frame
map scan. Preview and row lists are cached against the snapshot, settings and a revision counter.
At narrow widths the buttons wrap and the group rows collapse to a compact layout.

## Offline verification

`Tests/ManagementTests.cs` adds checks for defaults, settings save/load, control API success and
all refusal paths, stable IDs across reload, registry corruption/duplicates/pruning, group bulk
actions, bounded event log save/load, disabled policy, no batteries, EMP, generators/batteries
excluded, priority ordering, measured-savings selection, per-pass caps, stability, cooldown,
mid-band hysteresis, staged/all/manual recovery, destroyed buildings, controller integration,
component serialization, older saves without a management node, per-building diagnostic
exclusion and headless UI rendering at 320/640/1010/2560 px.

`Tests/Doubles/*` were extended with the verified flick/gizmo contracts, `Thing.GetUniqueLoadID`,
`Thing.thingIDNumber`, `Map.uniqueID`, `Faction.OfPlayer`, `Log.Warning` and a minimal
designation stand-in.

These checks pass in the `Offline logic tests` GitHub Actions job (Ubuntu, .NET 8), which
compiles the production sources against the doubles and runs the full harness.

**Not verified here:** compilation against official game assemblies, in-game switch behaviour
with real colonists/designations, real save/load with the game, rendering, and 1.7's API
signatures. See [PHASE-8-COMPATIBILITY.md](PHASE-8-COMPATIBILITY.md).

## In-game checklist (requires a game installation)

- [ ] New colony, default settings: Management tab says OFF; no building is ever switched.
- [ ] Enable controls only: on/off buttons work; gizmo checkbox and switch stay in sync; no
      `Flick` designation lingers; colonists do not re-flick the building.
- [ ] Toggle a switch quickly several times; no exception or log spam.
- [ ] Enable automation with zero sheddable buildings: preview is empty, nothing switches.
- [ ] Mark two non-critical consumers sheddable with different priorities; verify the preview
      lists both and the low-priority (higher number) one is shed first.
- [ ] Drain batteries below the threshold with a deficit: confirm stability/cooldown, then a
      bounded batch; verify the event log.
- [ ] Hold charge between thresholds: confirm no restore (hysteresis band).
- [ ] Recharge above the recovery threshold: confirm staged/all/manual behaviour.
- [ ] Disable automation mid-shedding: confirm every auto-shed building is restored.
- [ ] Destroy, uninstall and replace a configured building: no stale control, no crash; the
      replacement is protected until configured again.
- [ ] Exclude a building from diagnostics and confirm its building-level advisory disappears
      while network advisories remain.
- [ ] Save/reload and restart the game with a policy armed; verify settings, groups, priorities,
      exclusions, event history and tracked switches.
- [ ] Install with no DLC and with modded consumers/generators/batteries; verify unknown
      components are labelled unsupported and are never controlled.
- [ ] Compare a control save with the mod enabled/disabled: vanilla power totals are identical
      when management is off.
