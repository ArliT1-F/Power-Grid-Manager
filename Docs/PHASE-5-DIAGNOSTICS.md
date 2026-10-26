# Phase 5 — Power diagnostics

Phase 5 turns the measurements of Phase 2 and the history of Phase 4 into **advisories**:
statements about what the observed power data implies, with the evidence behind them and a
suggested action. Phase 4 explicitly recorded deficits without interpreting them; this phase
is the interpretation layer.

Everything here is read-only. The diagnostics engine reads the snapshot and the sample list.
It never writes to the power simulation, never flicks a switch, and never allocates a
background thread.

## Scope and rules

The design follows the same constraints as the rest of the mod:

- **No invented APIs.** Only members that exist in the game's public surface are read:
  `CompPowerTrader.PowerOutput` / `.PowerOn`, `CompPowerBattery.StoredEnergy` /
  `.StunnedByEMP` / `.Props.storedEnergyMax` / `.Props.efficiency`, `CompFlickable.SwitchIsOn`,
  and the `PowerNet` lists. Anything else is reported as *unsupported* rather than guessed.
- **No per-frame work.** The evaluator runs once per refresh from
  `PowerGridManagerComponent.EvaluateAdvisories`, i.e. at the refresh interval (default 120
  ticks), never from a draw pass. The UI only filters and formats a list that already exists.
- **Bounded memory.** The advisory list is capped at `PowerDiagnostics.MaxAdvisories` (80)
  plus resolved entries; building-level kinds are additionally capped at 12 rows each;
  episodes are keyed by live object identity and swept when their object disappears or their
  resolution ages out.
- **Diagnostics are separable.** Turning them off in the settings stops the evaluator and
  releases episode memory; monitoring and history keep working unchanged.
- **Session-only state.** Episodes, dismissals and announcements are never persisted. A
  reload starts from a clean slate rather than reporting a duration measured across a save.
  The one thing that *is* persisted is the set of muted advisory kinds, because that is a
  preference.

## Advisory kinds

| Kind | Severity | What it means |
| --- | --- | --- |
| `PersistentDeficit` | Warning → Critical after the configured window | Consumption exceeds generation on a network with no stored energy to cover it. |
| `HighLoad` | Info → Warning after the configured window | Consumption is at or above the high-load threshold of current generation. Generation still covers demand, so this is a warning, not a fault. |
| `BatteryDischarge` | Warning, or Critical when the bank is low, empties within two hours, or has been draining past the configured window below half charge | Batteries are covering a deficit. |
| `NoGeneration` | Critical, or Warning while the network still runs on stored energy | A network has consumers and nothing is generating. |
| `IsolatedConsumer` | Warning | A consumer has a power component but no network assignment. |
| `PendingMembership` | Info | Network membership could not be confirmed for the last reading. |
| `GeneratorUnderOutput` | Warning | A switched-on generator has produced far below its configured output, continuously, for the configured window. |
| `DrawAboveConfigured` | Info | Measured draw is well above the configured draw. Informational because variable consumers are legitimate. |
| `SwitchedOffButDrawing` | Warning | The flick switch is off, yet the building still reports a draw. |
| `UnsupportedComponent` | Info | A power component is not a recognised type, so its behaviour is not interpreted. |

Severity is not arbitrary: a deficit that has not yet lasted the configured window is a
warning rather than a critical, because RimWorld's own brownout and battery behaviour is
bursty. A generator that is switched off is not reported at all — producing nothing is the
expected result.

## Episode memory

`PowerDiagnosticTracker` remembers, per live `PowerNet` or `Building` and per kind, when a
condition was first seen, when it was last seen, and when it stopped being true. Consequences:

- **A condition that flickers is one episode**, not a new alert each time. Returning inside
  the cooldown window continues the same episode; returning after it starts a new one.
- **A condition that clears is reported as resolved** for one in-game day, with the numbers
  recorded while it was still true, rather than silently vanishing. The player can tell
  "fixed" apart from "no longer being looked at".
- **Episodes are released**: an episode whose object was destroyed or despawned is dropped
  immediately; a resolved episode is dropped once it ages past `ResolvedRetentionTicks`. The
  tracker never holds a reference that would keep a dead thing alive.

## Barring nuisance

Three defaults exist specifically to stop the feature from being annoying:

- `GeneratorUnderOutputTicks` defaults to a full in-game day. Solar produces nothing at
  night and wind idles in calm weather; a short delay would raise an advisory every single
  evening. The advisory text still tells the player to rule those causes out first.
- `AlertCooldownTicks` defaults to a full in-game day and gates *announcements*, not
  messages within one burst. Up to `MaxMessagesPerPass` (2) messages are sent when several
  new problems appear at once, and then nothing until the cooldown expires.
- `DeficitAlertWatts` (default 50 W) ignores trivially small shortfalls.

On-screen messages are additionally gated behind `ShowPowerMessages`, are only sent while
`Current.ProgramState == ProgramState.Playing` (the dashboard can trigger a refresh during
start-up), and never fire for Info or resolved advisories. The Diagnostics tab always holds
the full list, so turning messages off loses nothing.

## Muting and dismissing

- **Mute** hides one advisory kind everywhere. It is stored in the settings bitmask, masked
  on load to the kinds this build defines so a mask from a newer version cannot mute
  something unknown.
- **Dismiss** hides one specific advisory (kind + network or building) until it resolves, or
  until the player restores hidden advisories. It is session-only by design: a dismissal
  that outlived a save would hide a real problem the player has forgotten about.

## Reading an advisory

Each row carries five distinct things, deliberately separated so the player can audit the
conclusion:

- **Severity pill** — critical / warning / info, colour-coded.
- **Title** — what the condition is, and where.
- **Description** — what was observed, in numbers.
- **Evidence** — the measurements the conclusion rests on, including how long it has been
  true and how many samples support it.
- **Action** — what to do about it, phrased as advice, never performed automatically.

The row also states whether the condition is ongoing or resolved, and for how long.

## Limitations

- Network advisories quote map-wide history as evidence only when the map has exactly one
  network; otherwise an average over the whole map cannot be attributed to a single network.
- A diagnosis describes the reading, not the cause. "No generation" does not distinguish a
  broken-down generator from an unfuelled one; that is what the action text and the building
  table are for.
- A diagnosed deficit does not manifest instantly in game: `PowerNet` restarts browned-out
  consumers progressively (every 30–200 ticks, 5 % of them per event), so the visible
  consequence lags the diagnosis.
- Advisories are produced at the refresh interval, so a condition that appears and disappears
  between two refreshes may never be observed.

## Verification performed offline

`Tests/DiagnosticsTests.cs` (119 checks) covers: settings defaults and save/load clamping
including the mute mask; episode registration, duration, resolution, cooldown and reset;
every network advisory kind with its severity and evidence; every building advisory kind;
muting; the info toggle; the `DiagnosticsEnabled` master switch releasing episode memory; the
advisory cap with 90 failing networks; dismissal and restore; resolved-advisory reporting and
expiry; notification throttling (burst cap, no repeat announcements, messages off, Info and
resolved skipped, not announced before the game is playing); and a headless pass over the
diagnostics tab at 320x180, 1024x768 and 2560x1400 that clicks the filter, mute, dismiss,
restore, locate and show-buildings buttons and asserts that no draw path opens a window; and
a smoke pass over the settings window, which now carries the diagnostics, forecast and mute
sections.

Not verified: appearance in game, performance with hundreds of advisories, and interaction
with modded power systems that bypass `CompPowerTrader` (those are reported as unsupported
rather than diagnosed).
