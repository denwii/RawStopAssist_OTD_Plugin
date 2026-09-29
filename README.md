# RAW Stop Assist v0.8 — Micro Restart

An ultra-light **restart-only** OpenTabletDriver filter for osu!standard.

v0.8 keeps the stop/restart detector from v0.7, but removes the part that could make the cursor feel
heavy: there is **no Restart Dwell, no prepared positional delay, no history playback, no Recovery
Speed, and no catch-up state**.

Normal aim is exactly RAW. A detected stop only arms the filter. The effect begins on the first
movement away from that stop and is intentionally tiny.

> The filter only sees tablet movement. It does not know where osu! hit circles are or when you click.

## What changed from v0.7

Kept:
- the v0.7 noise-adaptive stop detector;
- ~12 ms stillness confirmation;
- the 9 ms restart-growth window and 14 ms resettle logic;
- tablet-size-aware thresholds in millimetres;
- the report-timing smoother used by the detector;
- RAW behaviour during ordinary movement and flow aim.

Removed:
- Restart Dwell;
- delay accumulation while stopped;
- time-shifted trajectory/history playback;
- Recovery Speed;
- Hold-as-a-full-delay phase;
- recovery/catch-up logic.

Added:
- a one-report **micro smoothing** that can only start after an armed stop;
- Devocub-style speed adaptation;
- only **two settings: Strength and Hold**.

## Devocub-style Strength

Devocub documents its antichatter as a speed-dependent power curve: slower motion gets more
smoothing and faster motion gets less. Its Strength changes how sharply the curve falls toward RAW.

v0.8 uses the same *idea* only for the tiny restart window, not the full Devocub filter. Internally the
shape is:

`curve = (1 + speed / scale)^(-Strength)`

This means:
- **lower Strength** = the tiny smoothing survives to somewhat higher restart speeds;
- **higher Strength** = the cursor becomes RAW more sharply as speed rises;
- **Strength 0** = pure RAW bypass.

Typical Devocub-like values are around **1–10**. Decimals are supported.

Reference: OpenTabletDriver's Devocub port and the original Devocub documentation describe the
same low-speed-more / high-speed-less power-law concept.

## Hold

Hold is only the **maximum window** after the first movement away from a detected stop.

It is not a flat hold. Even before Hold expires, the effect decreases automatically because:
1. increasing pen speed pushes the Devocub-style curve toward RAW;
2. a smooth time envelope fades the effect to zero by the end of Hold.

There is no separate recovery. Once the window ends, output is RAW.

Recommended starting point for a nearly-RAW feel: **Strength 3, Hold 2–3 ms**.

## How light is it?

The runtime effect is deliberately bounded. v0.8 keeps at most **0.10 ms of effective one-report
positional delay**, and normally much less because the speed curve immediately reduces it as the pen
accelerates. It also never retains more than 20% of a single RAW report step.

Unlike v0.7, it cannot build several milliseconds of stored lag while you are stopped, because there is
no delay buffer at all.

## Settings

| Setting | Range | Default | Meaning |
|---|---:|---:|---|
| **Strength** | 0–20 | 3 | Devocub-style sharpness. Lower = a little smoother; higher = RAW sooner. 0 = RAW |
| **Hold** | 0–50 ms | 3 ms | Maximum restart-only assist window. 0 = RAW |

Suggested tests:
- **3 / 2 ms** — very short, nearly RAW;
- **3 / 3 ms** — default;
- **2 / 3 ms** — slightly more persistent at low restart speed;
- **5 / 3 ms** — sharper / more RAW;
- **3 / 1 ms** — almost only the first report or two at ~1000 Hz.

## Installation

Build the project and place `RawStopAssistV08.dll` in its own OpenTabletDriver plugin folder, or package
the DLL and this README in a ZIP for the Plugin Manager.

Use only one RAW Stop Assist version at a time.

Target: OpenTabletDriver 0.6.x / .NET 8, including OTD 0.6.7.

## Build

```bash
dotnet build -c Release
```

Output:

`bin/Release/net8.0/RawStopAssistV08.dll`
