# Phase 3: dashboard

Status: implemented and verified against the offline harness only. No RimWorld
installation was available, so in-game rendering, interaction and layout have not been
observed. Nothing here should be read as an in-game validation claim.

## Entry point and API verification

The dashboard is a native main tab, added by a `MainButtonDef` in
`Defs/MainButtonDefs/PowerGridManager.xml`:

```xml
<MainButtonDef>
  <defName>PowerGridManager</defName>
  <label>Power</label>
  <tabWindowClass>PowerGridManager.MainTabWindow_PowerGridManager</tabWindowClass>
  <order>90</order>
  <iconPath>UI/Icons/PowerGridManager</iconPath>
</MainButtonDef>
```

Verified against the public 1.6 decompilation reference (not an installed game):

- `MainButtonDef.tabWindowClass` is instantiated with `Activator.CreateInstance` and the
  `MainButtonDef` is assigned to `MainTabWindow.def`; the tab window must derive from
  `MainTabWindow`.
- `MainTabWindow.RequestedTabSize` is virtual (`new Vector2(1010f, 684f)` by default) and
  `Window.InitialSize` clamps it to the screen, so overriding it is the supported way to
  size the dashboard.
- `MainTabWindow` already sets `WindowLayer.GameUI`, disables the close button/X and
  camera-motion blocking. The dashboard keeps that behaviour.
- `TabDrawer.DrawTabs<TTabRecord>(Rect baseRect, List<TTabRecord> tabs, float maxTabWidth)`
  draws the strip **32 px above** `baseRect`; the window therefore passes
  `inRect.y + 32f` and starts content 36 px below `inRect.y`.
- `TabRecord.GetTip()` is virtual, so tab tooltips use a small subclass instead of patching.
- Def `label`/`description` fields are translated through `DefInjected`, **not** `Keyed`.
  The def therefore carries display text while every player-facing string produced by C#
  goes through `Keyed` keys.
- `Map.FillComponents()` instantiates every non-`CustomMapComponent` subclass of
  `MapComponent` with a `(Map)` constructor. The monitoring component is discovered this
  way; no Harmony patch, def or manual registration is needed.

## Reading data (never scanning during a frame)

`MainTabWindow_PowerGridManager.DoWindowContents` only reads
`map.GetComponent<PowerGridManagerComponent>().Snapshot`. `PostOpen()` calls
`RefreshIfStale(tick)`, which rescans only when the cached snapshot is missing or older
than the configured interval. Drawing a frame never scans, sorts the full map or allocates
per-row objects:

- `BuildingListModel` rebuilds only when the snapshot reference, a filter revision, the
  sort key/direction, the grouping or the high-consumption threshold changes.
- Column widths, chart geometry and menu option lists are cached/reused; menu lists are
  only built when a menu is opened.
- No LINQ, no reflection, no background threads.

## Layout

| Tab | Contents |
|---|---|
| Overview | 20 metric tiles (5 rows x 4) plus an explanation panel stating exactly what the aggregates include. Tiles shrink on small windows and drop to the small font rather than overflow. |
| Networks | Network list (id, net balance, status pill, consumer/generator counts, battery count, stored/capacity, charge %) and a detail pane (all network statistics, charge bar, incomplete-data warning, largest consumers, show-buildings and locate buttons). |
| Buildings | Search box, sort menu, filter menu, category/grouping controls (collapsed into one "View" menu under 760 px), an N-of-M counter, the table, and a selected-building detail card. |
| History | Range buttons (hour / 6 h / day / 3 days / 7 days), sample summary, four charts (see Phase 4). |

### What each aggregate means (and is labelled as)

| Tile | Meaning |
|---|---|
| Current generation / consumption | Measured signed output of switched-on traders that are confirmed network members. |
| Net balance | Generation minus consumption. Trader balance only: excludes battery losses, self-discharge and debug unlimited power. |
| Consumption / generation | Ratio of the two measured values; unknown when nothing generates. |
| Configured generation | Sum of the generation declared in building defs. Shown in the muted "configuration, not measurement" style and never used in a calculation. Unknown when any generator does not declare one. |
| Stored energy / capacity / charge | Battery watt-days (energy), summed over all networks; EMP-disabled batteries still count in physical storage. |
| Networks / consumers / generators / batteries | Topology counts of components, not of buildings. |
| Deficit networks / high-load networks | Networks whose measured balance is negative, or whose consumption reaches the configured share of current generation. |
| Disconnected consumers | Consumers with no network assignment and no membership. A momentary topology change can look like this for one refresh. |
| Data notices | Count of data-quality notices (unsupported components, unconfirmed membership, disconnected consumers). One building can raise several; this is a notice count, not a building count. |
| Nominal draw | Configured consumption from defs, for comparison with measured draw. Hidden when incomplete. |
| Unsupported components / switched-off consumers / history interval | Supporting counts and the active sampling cadence. |

Installed generation capacity is **not** presented as if it were production. The
configured-generation tile is the closest supportable figure and is explicitly qualified
in its tooltip; the explanation panel repeats that point.

## Building table

Columns: building (with an `on`/`off` flick marker), category, measured draw, measured
output, network id, status. Status is text, not colour alone: `Unsupported`,
`Multiple networks`, `No network`, `Pending`, `Switched off`, `Powered down`,
`High consumption`, `OK`.

- Sorting: current draw, generation, name, category, status, plus a reverse-direction item.
  Direction applies once to a natural comparison, then ties break on label and finally on
  original scan order, so rows do not reshuffle between refreshes.
- Filtering: consumers/generators/batteries by role, network, category, flickable,
  switched-off, high-consumption, data notices, unknown behaviour, colonist-only, no-room,
  and free-text search over defName, label and category label (case-insensitive).
  A building whose only power components are unrecognised is still listed (it is not
  hidden) and is filtered out only when the consumer role is disabled.
- Detail card: measured draw, nominal draw, generation, trader state (On/Off/Mixed/not
  applicable), category, network, position, room, estimated daily energy, an explicit
  "history: not recorded" entry with a tooltip, stored/capacity for batteries, and support
  level. Buttons jump the camera to the building, open the vanilla info card, or clear the
  selection. Layout switches to a single column under 700 px.
- Selecting a building validates liveness every frame: a destroyed or despawned building
  clears the selection and shows an explanation rather than throwing.

Rooms come from `Thing.GetRoom()` (`RegionAndRoomQuery`); a building with no room is shown
as "No room assigned" and grouped separately, never attached to a guessed room.

Categories are conservative: battery -> storage, generator -> generation, then
`CompTempControl` -> temperature, `CompGlower` -> lighting, research bench / turret /
medical bed by class, trap -> security, then four vanilla thing-category defNames
(production, temperature, security, joy). Everything else stays uncategorized.

## Interaction and quality rules honoured here

- No modal dialogs: sorting, filtering and grouping use vanilla float menus.
- Searching updates the cached model through a revision counter, so typing is not
  re-filtered per frame beyond the single rebuild.
- Status is always a word plus a colour; deficits also carry an explicit text label.
- Scrolling is used for both lists; empty states have their own messages for
  "no power networks", "no powered buildings", "no matches", and "waiting for first
  reading".
- Every string produced in C# is a `PGM_*` keyed entry; the harness fails the build if a
  referenced key is missing or a format placeholder is short.

## Deliberate limitations

1. **No per-building history.** Only map aggregates are sampled, so per-building historical
   averages and exact daily energy are not offered; the daily figure is an explicit
   estimate from current draw.
2. **No installed-capacity guarantee.** Only configured generation is shown, qualified.
3. **Diagnostics are not part of this phase.** "Data notices" are observed data-quality
   facts; actionable power advisories (deficits, discharge warnings, thresholds, cooldowns)
   belong to Phase 5.
4. **Networks are identified by snapshot position.** The game exposes no stable network
   identity, so selection is revalidated each frame and never persisted.
5. **Network visuals are not drawn.** The detail pane offers "locate", which jumps to the
   network's largest consumer because that building is a confirmed member; it does not
   claim to render the network itself.
