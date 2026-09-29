using System;
using System.Diagnostics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace RawStopAssistV08;

[PluginName("RAW Stop Assist v0.8 · Micro Restart")]
public class RawStopAssistFilter : IPositionedPipelineElement<IDeviceReport>
{
    private readonly RawStopAssistEngine engine = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly object gate = new();

    [Property("Strength"), DefaultPropertyValue(RawStopAssistEngine.DefaultStrength)]
    [ToolTip("Devocub-style restart strength (0–20). 0 = pure RAW.\n\n" +
             "The filter acts only when you START moving away from a detected stop.\n" +
             "Lower values keep a little more low-speed smoothing; higher values become RAW\n" +
             "more sharply as speed rises. Typical Devocub-like range: about 1–10.\n\n" +
             "This is intentionally ultra-light: there is no dwell buffer and no continuous smoothing.")]
    public float Strength { get; set; } = RawStopAssistEngine.DefaultStrength;

    [Property("Hold"), Unit("ms"), DefaultPropertyValue(RawStopAssistEngine.DefaultHoldMs)]
    [ToolTip("Maximum time the restart-only micro smoothing may exist after leaving a detected stop (0–50 ms).\n\n" +
             "It is NOT a flat hold: the effect already fades toward RAW with speed and with time.\n" +
             "There is no Recovery setting and no catch-up phase. 0 = pure RAW.\n" +
             "For a nearly-RAW feel, start around 2–3 ms.")]
    public float HoldMilliseconds { get; set; } = RawStopAssistEngine.DefaultHoldMs;

    [TabletReference]
    public TabletReference Tablet
    {
        set
        {
            var digitizer = value?.Properties?.Specifications?.Digitizer;
            if (digitizer != null && digitizer.Width > 0 && digitizer.MaxX > 0)
            {
                lock (gate)
                    engine.UnitsPerMm = digitizer.MaxX / digitizer.Width;
            }
        }
    }

    // Keep the same placement as v0.7: operate on RAW tablet coordinates before screen mapping.
    public PipelinePosition Position => PipelinePosition.PreTransform;

    public event Action<IDeviceReport> Emit = delegate { };

    public void Consume(IDeviceReport value)
    {
        if (value is ITabletReport report)
        {
            lock (gate)
            {
                report.Position = engine.Process(
                    report.Position,
                    clock.Elapsed.TotalMilliseconds,
                    Math.Clamp(Strength, 0f, RawStopAssistEngine.MaxStrength),
                    Math.Clamp(HoldMilliseconds, 0f, RawStopAssistEngine.MaxHoldMs));
            }
        }

        Emit(value);
    }
}
using System;
using System.Diagnostics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace RawStopAssistV07;

[PluginName("RAW Stop Assist v0.7 · Restart Only")]
public class RawStopAssistFilter : IPositionedPipelineElement<IDeviceReport>
{
    private readonly RawStopAssistEngine engine = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly object gate = new();

    [Property("Restart dwell"), Unit("ms"), DefaultPropertyValue(3f)]
    [ToolTip("How far behind the pen the cursor may lag when you leave a circle (0–50 ms). 0 = pure RAW.\n\n" +
             "When you restart after a stop, the cursor shows where the pen was this many ms ago,\n" +
             "so it stays on the circle for that long. Positions are interpolated between reports:\n" +
             "at 133 Hz, 3 ms holds back ~40% of the first step and 7.5 ms holds back the whole first report.")]
    public float DwellMilliseconds { get; set; } = 3f;

    [Property("Hold time"), Unit("ms"), DefaultPropertyValue(3f)]
    [ToolTip("How long the full Restart dwell is kept after the pen moves off, before the cursor\n" +
             "starts catching up to RAW (0–100 ms).\n\n" +
             "Counted from the moment the pen leaves the stop point.\n" +
             "Recovery never starts before the restart is confirmed (2 reports: ~2 ms at 1000 Hz, ~15 ms at 133 Hz).\n\n" +
             "Hold = Restart dwell: the cursor leaves the circle after ~Restart dwell.\n" +
             "Lower: catch-up starts earlier and feels smoother. Higher: more time on the circle,\n" +
             "but the cursor stays behind longer on the way to the next one.")]
    public float HoldMilliseconds { get; set; } = 3f;

    [Property("Recovery speed"), DefaultPropertyValue(1f)]
    [ToolTip("How quickly the cursor catches up with the pen after the hold (0.25–3).\n\n" +
             "Catch-up time = 2 × lag ÷ Recovery speed (at least 2 reports).\n" +
             "Peak cursor speed while catching up = (1 + Recovery speed) × pen speed.\n\n" +
             "1 = default (20 ms of lag is recovered in 40 ms).\n" +
             "2 = twice as fast and snappier. 0.5 = softer, but the lag lasts longer.")]
    public float RecoverySpeed { get; set; } = 1f;

    [Property("Strength"), DefaultPropertyValue(1f)]
    [ToolTip("How fast the restart delay builds up while you stand still on a circle (0–2). 0 = pure RAW.\n\n" +
             "Once a real stop is detected, the delay grows by 0.5 × Strength ms per ms of standing still,\n" +
             "up to Restart dwell. Full delay is ready after about 12 ms + Restart dwell ÷ (0.5 × Strength).\n" +
             "Higher values give a full hold even after short stops. Slowing down without stopping never\n" +
             "triggers the filter.\n\n" +
             "1 = 0.5 ms per ms (the cursor slows gently while the delay builds up).\n" +
             "2 = 1 ms per ms (the cursor briefly pauses, always within ~0.1–0.25 mm of the pen).")]
    public float Strength { get; set; } = 1f;

    [TabletReference]
    public TabletReference Tablet
    {
        set
        {
            var digitizer = value?.Properties?.Specifications?.Digitizer;
            if (digitizer != null && digitizer.Width > 0 && digitizer.MaxX > 0)
            {
                lock (gate)
                    engine.UnitsPerMm = digitizer.MaxX / digitizer.Width;
            }
        }
    }

    // PreTransform: lavora sulle coordinate grezze del tablet, prima della mappatura sullo schermo.
    public PipelinePosition Position => PipelinePosition.PreTransform;

    public event Action<IDeviceReport> Emit = delegate { };

    public void Consume(IDeviceReport value)
    {
        if (value is ITabletReport report)
        {
            lock (gate)
            {
                report.Position = engine.Process(
                    report.Position,
                    clock.Elapsed.TotalMilliseconds,
                    Math.Clamp(DwellMilliseconds, 0f, RawStopAssistEngine.MaxDwellMs),
                    Math.Clamp(Strength, 0f, RawStopAssistEngine.MaxStrength),
                    Math.Clamp(RecoverySpeed, RawStopAssistEngine.MinRecoverySpeed, RawStopAssistEngine.MaxRecoverySpeed),
                    Math.Clamp(HoldMilliseconds, 0f, RawStopAssistEngine.MaxHoldMs));
            }
        }

        Emit(value);
    }
}
