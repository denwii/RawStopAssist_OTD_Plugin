A **restart-only** OpenTabletDriver filter for osu!standard.

Normal aim is exactly RAW. A detected stop only arms the filter. When the pen starts moving away
from that stop, the cursor is held back for **Hold** milliseconds with a firmness set by
**Strength**, then it catches up and the output is RAW again.

> The filter only sees tablet movement. It does not know where osu! hit circles are or when you click.

## What changed from v0.8

v0.8 could not delay the cursor by more than 0.10 ms of movement (a fraction of a pixel), and a
higher Strength made it weaker. v0.9 replaces that with a real hold:

- **Strength now means "more"**: higher Strength always holds the cursor more.
- **Hold is a real duration**: Strength is applied at full value for the whole Hold.
- the hold uses Devocub's antichatter weighting, so the cursor sticks while the pen is still close
  and lets go once the pen has pulled away;
- the stop detector is time-based, so it behaves the same at the tablet's native rate and behind a
  1000 Hz interpolating filter such as Devocub Antichatter;
- slow steady motion (slider follow) no longer counts as a stop, and the noise estimate is no longer
  inflated by slow drift.

Still true:

- only **two settings: Strength and Hold**;
- no dwell buffer, no history playback, no Recovery setting;
- standing still never adds delay.

## Strength

While the hold lasts, the cursor follows the pen with Devocub's antichatter weight:

`weight = base / (1 + (knee / lag)^3)`, with `knee = Strength × 0.1 mm`

`lag` is the distance between the cursor and the pen. While the pen is closer than the knee the
cursor barely moves; once the pen is past it, the cursor follows.

- **Strength 0** = pure RAW bypass;
- **Strength 10** = knee at 1 mm, the reach of Devocub's own antichatter;
- **Strength 20** = knee at 2 mm.

On a 78 mm wide area mapped to 1920 px, 0.1 mm is about 2.5 px, so each Strength step is roughly
2.5 px of extra stick.

## Hold

Hold is how long Strength is applied, counted from the first movement away from the detected stop.

When Hold ends, the remaining lag collapses in about 2–3 ms and the output is bit-exact RAW again.
The hold also ends early if the pen comes back to rest.

Longer Hold with high Strength means a bigger catch-up at the end, because the cursor has been
held further behind the pen.

## Settings

| Setting | Range | Default | Meaning |
|---|---:|---:|---|
| **Strength** | 0–20 | 5 | How firmly the cursor is held. Higher = more. 0 = RAW |
| **Hold** | 0–50 ms | 8 ms | How long Strength is applied after leaving a stop. 0 = RAW |

Suggested tests:
- **5 / 8 ms** — default, light;
- **10 / 8 ms** — firmer, same duration;
- **10 / 15 ms** — firm and longer;
- **20 / 20 ms** — very strong;
- **5 / 3 ms** — just a short touch.

## When does it trigger?

A stop is confirmed when the pen has stayed within a small radius (0.08–0.25 mm, adapted to the
pen's jitter) for about 12 ms without drifting. The hold starts on the first report that leaves
that radius. Flow aim and steady movement never arm the filter.

At the tablet's native report rate (133 Hz on a CTL-472) a report arrives every 7.5 ms, so the hold
can only act in steps of that size. Behind a 1000 Hz filter it acts every millisecond.

## Installation

Build the project and place `RawStopAssistV09.dll` in its own OpenTabletDriver plugin folder, or package
the DLL and this README in a ZIP for the Plugin Manager.

Use only one RAW Stop Assist version at a time.

Target: OpenTabletDriver 0.6.x / .NET 8, including OTD 0.6.7.

## Build

```bash
dotnet build -c Release
```

Output:

`bin/Release/net8.0/RawStopAssistV09.dll`
