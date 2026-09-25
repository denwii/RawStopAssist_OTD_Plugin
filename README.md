# RAW Stop Assist v0.6

An **OpenTabletDriver 0.6.x** filter for osu!standard that helps you avoid **overshoot misses**.
When you leave a circle, whether from a full stop or a flow-aim slowdown, the cursor stays on the
circle for a few extra milliseconds, then catches up smoothly. The rest of the time the output is
pure **RAW**: no continuous smoothing and no added latency while you aim.

> The filter only sees pen movement. It does not know where hit circles are or when you click.

---

## How it works

The cursor always follows the **exact path of the pen**. The filter only changes *when* the
cursor reaches each point on that path, by showing where the pen was a few milliseconds ago.

1. **Moving → RAW.** Output equals input.
2. **You stop on a circle → the filter arms itself.** Once the pen has been still for ~12 ms, the
   cursor starts trailing the pen by up to *Restart dwell* ms. While the pen is still, this lag
   is invisible, and it never times out. You can sit on a stack as long as you like.
3. **You move off → the cursor stays on the circle.** From the very first report of the restart
   the cursor is already behind, so it stays put for about *Restart dwell* ms. No prediction and
   no late freeze.
4. **Catch-up → back to RAW.** After *Hold time*, the lag shrinks to zero along an ease-out
   curve. There are no jumps or teleports, and no speed step when the cursor rejoins the pen.

**Flow aim** works the same way. After a jump, when the pen slows sharply near a circle without
stopping, the cursor slows down a bit more than the pen. It then speeds up a bit harder when you
re-accelerate toward the next circle, which feels snappier. Streams and slow aim are left alone.

---

## Installation

1. Close OpenTabletDriver.
2. Copy the `Raw_Stop_Assist_OTD_v0.6.0` folder into
   `%LOCALAPPDATA%\OpenTabletDriver\Plugins\`.
3. Restart OTD and enable **RAW Stop Assist v0.6 · Restart Only** in the Filters tab.

**Enable only one RAW Stop Assist version at a time.** Older versions can stay installed, but
their effects stack if more than one is enabled.

Requires OTD 0.6.x on .NET 8 (e.g. 0.6.7).

---

## Settings

| Setting | Range | Default | What it controls |
|---|---|---|---|
| **Restart dwell** | 0–50 ms | 3 | *How much* lag: how long the cursor stays on the circle. 0 = RAW |
| **Hold time** | 0–100 ms | 3 | *How long* the full lag is kept before catching up |
| **Recovery speed** | 0.25–3 | 1 | *How fast* the cursor catches up with the pen |
| **Strength** | 0–2 | 1 | *How strongly* the filter reacts to stops and flow slowdowns. 0 = RAW |

Decimals are allowed everywhere. If OTD rejects a decimal point, try a comma, or the other way
round; it depends on your Windows locale.

### Restart dwell

The cursor shows where the pen was **N ms ago**, interpolated between reports, so the value
means the same thing at any report rate. At 133 Hz (a report every ~7.5 ms), 3 ms holds back
about 40% of the first step and 7.5 ms holds back the whole first report. At 1000 Hz every
millisecond is visible.

### Hold time

How long the full lag is kept after the pen moves off, before the catch-up starts.

- Counted from the moment the pen leaves the stop point, or from the re-acceleration in flow aim.
- The catch-up never starts before the restart is **confirmed** (2 reports: ~2 ms at 1000 Hz,
  ~15 ms at 133 Hz). The effective hold is therefore the larger of *Hold time* and that
  confirmation time.

| Hold vs. Restart dwell | Effect |
|---|---|
| Equal | The cursor leaves the circle after about *Restart dwell* ms |
| Lower | The catch-up starts earlier and feels smoother; slightly less time on the circle |
| Higher | More time on the circle in flow, but the cursor stays behind longer on the way to the next circle |

Recommended: **between ~10 ms and your Restart dwell**. Much higher values can make the cursor
arrive late at the next circle on fast jumps.

### Recovery speed

- **Catch-up time** = 2 × lag ÷ Recovery speed, never shorter than 2 reports.
- **Peak cursor speed** while catching up = (1 + Recovery speed) × pen speed (theoretical limit).

| Recovery speed | Catch-up for 20 ms of lag | Peak speed limit |
|---|---|---|
| 0.5 | 80 ms | 1.5× |
| 1 | 40 ms | 2× |
| 2 | 20 ms | 3× |
| 3 | ~13 ms | 4× |

This is a trade-off. A faster recovery is snappier and gets you back to RAW sooner, but it also
shortens the time spent on the circle. Recommended: **1–2**.

### Strength

- **Flow aim:** after a jump (peak pen speed > 150 mm/s), once the pen drops below
  *Strength × 20%* of that peak, the cursor builds up lag at *0.5 × Strength* ms per ms, up to
  Restart dwell.
- **Stops:** the restart hold builds up at *0.5 × Strength* ms per ms of standing still. Full
  hold is reached after about **12 ms + Restart dwell ÷ (0.5 × Strength)** of standing still.

| Strength | Flow slowdown triggers below | Cursor speed during the slowdown |
|---|---|---|
| 0.5 | 10% of peak | 75% of the pen's |
| 1 | 20% of peak | 50% |
| 1.5 | 30% of peak | 25% |
| 2 | 40% of peak | 0% (the cursor stops) |

Recommended: **1–1.5**. At 2 the cursor can come to a full stop before it reaches the circle on
long jumps.

---

## Suggested starting points

| Style | Restart dwell | Hold time | Recovery speed | Strength |
|---|---|---|---|---|
| Subtle (feels almost RAW) | 3–5 ms | 3 | 1 | 1 |
| Balanced | 8 ms | 8 | 1–1.5 | 1 |
| Strong assist | 15–20 ms | 10–20 | 1–2 | 1–1.5 |

Tune one setting at a time. [FilterScope](#tools) shows RAW and filtered cursor speed in px/s,
which makes each change easy to see.

---

## Simulation results

Simulated Wacom CTL-472 with sensor noise, hand tremor and report-timing jitter, over 150 jumps
per scenario. The reference circle is osu! CS4 (≈ 3.4 mm radius on an 80 mm wide area).

**Restart dwell 20, Strength 1, Recovery speed 1, Hold time 20 (133 Hz):**

| | Result |
|---|---|
| Extra time on the circle after a stop | ~16 ms |
| Extra time on the circle in flow aim | +8 to +12 ms |
| Arrival at the next circle | on time (≤ 1 ms late on very fast jumps) |
| Streams | unaffected |
| Freezes while aiming | none |
| False restarts during 3 s stacks | none |

**Restart confirmation at 1000 Hz (v0.6):**

| | Before | Now |
|---|---|---|
| Slow restarts confirmed | 9 / 60 | 59 / 60 |
| Residual lag while moving (slow restarts) | 13.7 s total | 8.0 s total (−40%) |
| Fast jumps, short stops, curves, noisy stacks | — | identical |

*These are simulation results. The real test is playing.*

---

## What changed

**v0.6**
- New **Recovery speed** and **Hold time** settings.
- Restart confirmation now uses milliseconds instead of report counts, so it behaves the same at
  133 Hz and 1000 Hz. Slow restarts at 1000 Hz are now confirmed reliably.
- English tooltips; settings reordered: dwell → hold → recovery → strength.

**v0.5**
- **Strength**, including flow-aim support for slowdowns without a full stop.

**v0.4** (full rewrite of v0.3.3)
- The hold starts on the first report of the restart. v0.3.3 had a *moves → freezes → catches
  up* pattern.
- Noise while standing still no longer uses up the assist, and there is no cooldown.
- No freezes while aiming. Smooth catch-up instead of the old 2× chase and snap back to RAW.
- Thresholds in millimetres, read from the tablet specs; report timestamps are smoothed.

---

## Tools

**FilterScope** is a companion viewer that overlays the RAW (red) and filtered (blue) cursor,
with estimated lag and live **px/s speed** for both. It is the easiest way to see what each
setting does.

---

## Building from source

Requires the **.NET 8 SDK**. The `OpenTabletDriver.Plugin` package comes from NuGet.

```
dotnet build -c Release
```

Output: `bin\Release\net8.0\RawStopAssistV06.dll`.
