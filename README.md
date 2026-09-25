# RAW Stop Assist v0.7

An **OpenTabletDriver 0.6.x** filter for osu!standard that helps you avoid **overshoot misses**.
When you stop on a circle and then move off, the cursor stays on the circle for a few extra
milliseconds and then catches up smoothly. The rest of the time the output is pure **RAW**: no
smoothing, no added latency while you aim, and no reaction to slowing down without stopping.

> The filter only sees pen movement. It does not know where hit circles are or when you click.

---

## How it works

The cursor always follows the **exact path of the pen**. The filter only changes *when* the
cursor reaches each point on that path, by showing where the pen was a few milliseconds ago.

1. **Moving → RAW.** The output is identical to the input, including when you slow down and
   speed up again without stopping.
2. **You stop on a circle → the filter arms itself.** Once the pen has been still for ~12 ms
   (within a small, noise-adaptive radius), the restart delay builds up gradually, up to
   *Restart dwell*. While the pen is still, this delay is invisible, and it never times out.
   You can sit on a stack for seconds; sensor noise does not use up the assist.
3. **You move off → the cursor stays on the circle.** From the very first report of the restart
   the cursor is already behind, so it stays put for about *Restart dwell* ms. No prediction and
   no late freeze.
4. **Catch-up → back to RAW.** After *Hold time*, the delay shrinks to zero along an ease-out
   curve. There are no jumps or teleports, and no speed step when the cursor rejoins the pen.

---

## Installation

**Plugin Manager:** open the Plugin Manager in OpenTabletDriver and install
`Raw_Stop_Assist_OTD_v0.7.0.zip` from file (or drag the zip into the window). The zip contains
the DLL and this README at its root.

**Manual:**
1. Close OpenTabletDriver.
2. Copy the `Raw_Stop_Assist_OTD_v0.7.0` folder into
   `%LOCALAPPDATA%\OpenTabletDriver\Plugins\`.
3. Restart OTD.

Then enable **RAW Stop Assist v0.7 · Restart Only** in the Filters tab.

**Enable only one RAW Stop Assist version at a time.** Older versions can stay installed, but
their effects stack if more than one is enabled.

Requires OTD 0.6.x on .NET 8 (e.g. 0.6.7).

---

## Settings

| Setting | Range | Default | What it controls |
|---|---|---|---|
| **Restart dwell** | 0–50 ms | 3 | *How much* delay: how long the cursor stays on the circle. 0 = RAW |
| **Hold time** | 0–100 ms | 3 | *How long* the full delay is kept after you move off |
| **Recovery speed** | 0.25–3 | 1 | *How fast* the cursor catches up with the pen |
| **Strength** | 0–2 | 1 | *How fast* the delay builds up while you stand still. 0 = RAW |

Decimals are allowed everywhere. If OTD rejects a decimal point, try a comma, or the other way
round; it depends on your Windows locale.

### Restart dwell

The cursor shows where the pen was **N ms ago**, interpolated between reports, so the value
means the same thing at any report rate. At 133 Hz (a report every ~7.5 ms), 3 ms holds back
about 40% of the first step and 7.5 ms holds back the whole first report. At 1000 Hz every
millisecond is visible.

### Hold time

How long the full delay is kept after the pen leaves the stop point, before the catch-up starts.

- The catch-up never starts before the restart is **confirmed** (2 reports: ~2 ms at 1000 Hz,
  ~15 ms at 133 Hz). The effective hold is therefore the larger of *Hold time* and that
  confirmation time.

| Hold vs. Restart dwell | Effect |
|---|---|
| Equal | The cursor leaves the circle after about *Restart dwell* ms |
| Lower | The catch-up starts earlier and feels smoother; slightly less time on the circle |
| Higher | The cursor stays behind longer on the way to the next circle |

Recommended: **between ~10 ms and your Restart dwell** (at 1000 Hz, from ~3 ms). Much higher
values can make the cursor arrive late at the next circle on fast jumps.

### Recovery speed

- **Catch-up time** = 2 × delay ÷ Recovery speed, never shorter than 2 reports.
- **Peak cursor speed** while catching up = (1 + Recovery speed) × pen speed (theoretical limit).

| Recovery speed | Catch-up for 20 ms of delay | Peak speed limit |
|---|---|---|
| 0.5 | 80 ms | 1.5× |
| 1 | 40 ms | 2× |
| 2 | 20 ms | 3× |
| 3 | ~13 ms | 4× |

This is a trade-off. A faster recovery is snappier and gets you back to RAW sooner, but it also
shortens the time spent on the circle. Recommended: **1–2**.

### Strength

How fast the restart delay builds up **once a real stop has been detected**: *0.5 × Strength*
ms of delay per ms of standing still, up to Restart dwell.

| Strength | Build-up rate | Full delay ready after (Restart dwell 20) | While it builds up |
|---|---|---|---|
| 0.5 | 0.25 ms/ms | ~92 ms | cursor barely slows |
| 1 | 0.5 ms/ms | ~52 ms | cursor slows gently |
| 1.5 | 0.75 ms/ms | ~39 ms | cursor slows more |
| 2 | 1 ms/ms | ~32 ms | cursor briefly pauses (within ~0.1–0.25 mm of the pen) |

The delay can never be larger than the time you have actually stood still. Higher Strength gives
you a full hold even after **short stops**, which is useful on fast jumps. Strength does not
change when the filter activates: slowing down without stopping never triggers it.

---

## Suggested starting points

| Style | Restart dwell | Hold time | Recovery speed | Strength |
|---|---|---|---|---|
| Subtle (feels almost RAW) | 3–5 ms | 3 | 1 | 1 |
| Balanced | 8 ms | 8 | 1–1.5 | 1 |
| Strong assist | 15–20 ms | 10–20 | 1–2 | 1–2 |

Tune one setting at a time. FilterScope shows RAW and filtered cursor speed in px/s, which makes
each change easy to see.

---

## Simulation results (v0.7)

Simulated Wacom CTL-472 with sensor noise, hand tremor and report-timing jitter, over 150 jumps
per scenario, compared with v0.6.

| | 133 Hz | 1000 Hz |
|---|---|---|
| Hold after a stop (Restart dwell 20, Hold time 20) | ~16 ms | ~18 ms |
| Restarts confirmed (jumps, curves, short stops) | 150 / 150 | 150 / 150 |
| Slow restarts confirmed | 60 / 60 | 59 / 60 |
| Peak cursor speed while catching up | ≤ 1.5× the pen | ≤ 1.7× the pen |
| False restarts on 3 s stacks with normal noise | none | none |
| Strength 0 or Restart dwell 0 | output identical to input | output identical to input |

**Compared with v0.6:**
- Slowing down and speeding up without stopping no longer adds delay. In flow-style movement at
  133 Hz, the time with any delay active drops from 10–66% to 0–7%. The small remainder comes
  from sharp direction changes where the pen really comes almost to a halt.
- Stop → restart behaves the same, with one intended exception. After **very short stops**
  (15–35 ms) with a high Restart dwell, the hold is slightly shorter (e.g. 13.0 → 11.1 ms at
  133 Hz with Restart dwell 20). v0.6 started building the delay during the deceleration
  *before* the stop; v0.7 only builds it while you are actually still. Raise Strength if you
  want a full hold after short stops.

*These are simulation results. The real test is playing.*

---

## Known limitation at 1000 Hz

At 1000 Hz the filter can arm during slow continuous movement, such as streams, slow sliders or
sharp direction changes. In simulation this happens for 2–20% of the time with Restart dwell 8,
with the cursor at most ~0.6 mm behind the pen. With Restart dwell 20 it rises to 4–34% and a few
millimetres. At 133 Hz it stays at 0–7%. The same behaviour was already present in v0.6 and is
unrelated to the removed flow-aim mode. It comes from the noise estimate adapting per report
rather than per millisecond. A fix has been tested in simulation and is planned for a separate
update.

---

## What changed

**v0.7**
- **Removed flow-aim assistance.** The filter now reacts only to real stops: slowing down or
  speeding up without stopping stays RAW.
- **Strength** now only controls how fast the delay builds up during a detected stop.
- Removed all code, thresholds and internal state used only by flow aim.

**v0.6**
- New **Recovery speed** and **Hold time** settings.
- Restart confirmation uses milliseconds instead of report counts, so slow restarts at 1000 Hz
  are confirmed reliably.
- English tooltips; settings ordered dwell → hold → recovery → strength.

**v0.4** (full rewrite of v0.3.3)
- The hold starts on the first report of the restart. v0.3.3 had a *moves → freezes → catches
  up* pattern.
- Noise while standing still no longer uses up the assist, and there is no cooldown.
- No freezes while aiming; smooth catch-up instead of the old 2× chase and snap back to RAW.
- Thresholds in millimetres, read from the tablet specs; report timestamps are smoothed.

---

## Building from source

Requires the **.NET 8 SDK**. The `OpenTabletDriver.Plugin` package comes from NuGet.

```
dotnet build -c Release
```

Output: `bin\Release\net8.0\RawStopAssistV07.dll`.

dotnet build -c Release
```

Output: `bin\Release\net8.0\RawStopAssistV06.dll`.
