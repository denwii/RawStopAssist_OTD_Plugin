# RAW Stop Assist

A lightweight OpenTabletDriver filter designed for osu!standard that preserves the feeling of raw tablet input while applying a brief, customizable delay when you start moving again after a confirmed stop.

The goal is simple: help reduce occasional overshooting without introducing unnecessary smoothing during normal cursor movement.

## How It Works

RAW Stop Assist monitors your tablet's movement and detects when the pen comes to a genuine stop, accounting for small amounts of tablet noise.

When movement resumes, the filter briefly holds the cursor near its last position before smoothly catching up with the pen.

During normal movement, the cursor remains completely raw.

The filter does not require a change in movement direction and does not activate simply because the cursor decelerates.

### Movement Behavior

| Situation            | Behavior                                   |
| -------------------- | ------------------------------------------ |
| Normal movement      | RAW input                                  |
| Deceleration         | RAW input                                  |
| Confirmed stop       | RAW input                                  |
| Restart after a stop | Brief cursor hold                          |
| Recovery             | Smooth transition back to the pen position |
| Recovery complete    | RAW input                                  |

The filter does not read osu! beatmaps or detect hitcircles directly. It identifies movement patterns associated with stopping and restarting.

## Settings

Only one setting is required:

**Restart dwell (ms)**

Controls how long the cursor is briefly held when movement resumes after a confirmed stop.

| Value  | Behavior                   |
| ------ | -------------------------- |
| 0 ms   | Completely RAW             |
| 1–2 ms | Very subtle assistance     |
| 3–4 ms | Moderate assistance        |
| 5+ ms  | More noticeable assistance |

Default value: **3 ms**

The total intervention may last slightly longer than the configured dwell because the cursor needs time to catch up smoothly.

All movement detection parameters are handled internally.

## Installation

1. Download the latest plugin ZIP from the [Releases](../../releases) page.
2. Open OpenTabletDriver.
3. Drag the ZIP into the OpenTabletDriver Plugin Manager.
4. Enable RAW Stop Assist in your filters.
5. Adjust **Restart dwell (ms)** to your preference.

If necessary, restart OpenTabletDriver after installing the plugin.

### Requirements

* OpenTabletDriver 0.6.7
* Windows

## Why RAW Stop Assist?

Traditional smoothing filters continuously modify cursor movement, which can introduce unwanted latency during aiming, tracking, and micro-adjustments.

RAW Stop Assist takes a different approach.

Instead of smoothing everything, it only intervenes when the pen starts moving again after a detected stop.

This makes it particularly interesting for players who enjoy raw input but occasionally overshoot their targets.

## Important Notes

RAW Stop Assist is an experimental aim-assistance filter, not a guaranteed solution to overshooting.

Its effectiveness depends on your aiming habits, tablet configuration, and personal preferences.

It is designed to preserve raw input as much as possible, but the temporary hold and recovery intentionally alter cursor movement when activated.

## License

See the LICENSE file for licensing information.
