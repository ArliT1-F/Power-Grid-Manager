# Phase 6 — Battery endurance forecasting

Phase 6 answers one question: **given what the grid is doing right now — and, when history
exists, what it has been doing — how long until the colony's stored energy runs out, or until
it is full?**

The answer is an *estimate*, and the mod says so everywhere it appears. This document records
exactly what is measured, what is assumed, and where the estimate can be wrong.

## The one unit identity it rests on

RimWorld's `CompPower.WattsToWattDaysPerTick` is `1 / 60000`, so **one watt sustained for one
in-game day is one watt-day**. That is why stored energy and capacity can be compared directly
against a rate in watts with no conversion factor:

```
days = wattDays / watts
ticks = days * 60000
```

`PowerTime.TicksPerDay` (60,000) is the value used, not `Verse.GenDate`.

## Inputs

| Input | Source | Kind |
| --- | --- | --- |
| Stored energy, capacity | `CompPowerBattery.StoredEnergy`, `.Props.storedEnergyMax` | measured |
| Usable energy | stored energy, excluding EMP-stunned batteries | measured |
| Charge efficiency | `CompPowerBattery.Props.efficiency` (vanilla 0.5) | measured, falls back to "unconfirmed" |
| Generation and consumption | signed `CompPowerTrader.PowerOutput`, gated by `PowerOn` | measured |
| Historical average net balance | Phase 4 samples over the selected range | measured (aggregated) |
| Self-discharge | 5 W per battery that holds energy | **verified game constant**, not measured |

## What is included, and why

**Self-discharge.** `CompPowerBattery.CompTick` draws 5 W continuously from every battery that
has stored energy. That draw is *not* part of the trader balance the dashboard shows, so a
forecast built on the trader balance alone overstates endurance — badly on a small deficit:
a 20 W shortfall with four batteries is really a 40 W shortfall. The forecast subtracts it by
default (`ForecastIncludeSelfDischarge`), and the panel states when it has been left out so
the resulting optimism is visible rather than hidden.

**Charge efficiency.** Charging a battery discards `1 - efficiency` of the surplus. The time
to full therefore divides the surplus by the efficiency. When the efficiency cannot be read
the forecast assumes no loss — and says so, and caps the result at *medium* confidence.

**EMP.** A battery stunned by EMP contributes zero usable energy, matching the game's own
behaviour. Its physical storage is still reported; only the usable figure changes.

**What is deliberately not modelled.** No discharge rate limit. A public, per-battery
`MaxDischargeWatts` is not confirmed for the target builds, so inventing one would be exactly
the kind of guess this mod refuses to make. If the game does impose one, real endurance on a
large deficit will be *longer* than the estimate, and the estimate is therefore conservative.

## Structure

- `Analytics/PowerForecast.cs` — pure aggregates over the bounded sample list: average
  generation and consumption over a tick window, average net balance, and the share of
  samples that were in deficit. All return "unavailable" rather than zero when there is
  nothing to average.
- `Analytics/BatteryForecast.cs` — the arithmetic itself. No game objects, no simulation
  calls, no side effects: `Evaluate` takes numbers and returns numbers. `ForNetwork` and
  `ForMap` are thin adapters over the snapshot types.
- `UI/PowerForecastPanel.cs` — the Forecast tab.

## Results and confidence

`BatteryForecastResult.Kind` is one of `NoBatteries`, `NoCapacity`, `Balanced`, `Depleting`,
`Charging`. When no estimate is possible the panel explains why instead of showing a number —
an empty "0 days" would read as a prediction.

Confidence is stated, never implied:

| Confidence | Condition |
| --- | --- |
| `Unavailable` | No batteries, unreadable capacity, or a genuinely flat balance. |
| `Low` | Only the current balance is known (fewer than 4 samples), or the recorded history contradicts its direction. |
| `Medium` | History agrees in direction but disagrees materially in size, or a charging estimate whose efficiency is unconfirmed. |
| `High` | At least 4 samples in range, and the historical average agrees with the current balance within 25 %. |

Estimates beyond 30 in-game days are reported as "more than" rather than as a precise number,
because a 60-day projection from a snapshot of current demand is not information.

## Where it appears

- **Forecast tab** — range selector, four metric tiles (stored, capacity, charge, net
  including losses), the headline estimate, an explicit statement of the basis and
  confidence, a comparison of the current balance against the recorded average, per-network
  endurance, and a "what this estimate assumes" block.
- **Overview** — two tiles: endurance and confidence, both reading "unavailable" when no
  forecast exists rather than defaulting to zero.
- **Networks** — an endurance line per network, computed from that network's current balance
  only, with a tooltip saying so.

## Honesty rules enforced in code

1. An estimate never writes anything back. Forecasting reads the snapshot; it does not touch
   `PowerNet`, any comp, or any saved value. (`ForecastTests.DoesNotTouchTheSimulation`
   asserts this.)
2. An estimate never appears without its basis. Every headline is accompanied by whether it
   comes from the current balance alone or from the current balance plus N samples.
3. An unverifiable input downgrades the result rather than being silently assumed. An
   unconfirmed charge efficiency caps confidence at medium and is named in the notes.
4. Degenerate input never produces a number. NaN, infinite and negative readings are
   sanitized; stored energy above capacity is clamped; a missing snapshot yields
   "no batteries", not a crash or a zero.

## Verification performed offline

`Tests/ForecastTests.cs` (90 checks) covers: windowed averages and deficit shares, including
empty and inverted windows; depletion and charging arithmetic against hand-computed values;
self-discharge scaling with battery count; the effect of leaving self-discharge out; EMP;
saturation at the reporting horizon; NaN/negative/over-capacity inputs; every confidence
band; `ForNetwork`, `ForMap` and the map component's `ComputeForecast` with real samples;
a headless pass over the forecast and network panels at 320x180, 1024x768 and 2560x1400;
and the read-only guarantee.

## What would make this better

- A confirmed per-battery discharge rate limit (would tighten large-deficit estimates).
- Per-network history rather than map-wide history, which would let per-network endurance
  quote a basis other than the current reading. Both are deliberately out of scope for now:
  the current implementation reports the limitation instead of hiding it.
