using System;
using System.Diagnostics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace RawStopAssistV09;

[PluginName("RAW Stop Assist v0.9 · Restart Hold")]
public class RawStopAssistFilter : IPositionedPipelineElement<IDeviceReport>
{
    private readonly RawStopAssistEngine engine = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly object gate = new();

    [Property("Strength"), DefaultPropertyValue(RawStopAssistEngine.DefaultStrength)]
    [ToolTip("How firmly the cursor is held when you leave a stop (0–20). 0 = pure RAW.\n\n" +
             "Higher = stronger hold. The cursor stays stuck to the stop point until the pen has\n" +
             "pulled about Strength × 0.1 mm away, then follows it, Devocub-antichatter style.\n" +
             "Strength 10 matches the reach of Devocub's own antichatter (1 mm).\n\n" +
             "The filter acts only when you START moving away from a detected stop.\n" +
             "Everything else is exactly RAW.")]
    public float Strength { get; set; } = RawStopAssistEngine.DefaultStrength;

    [Property("Hold"), Unit("ms"), DefaultPropertyValue(RawStopAssistEngine.DefaultHoldMs)]
    [ToolTip("How long Strength is applied after the first movement away from a detected stop (0–50 ms).\n\n" +
             "Strength stays at its full value for the whole Hold. When Hold ends the cursor\n" +
             "catches up with the pen within about 2–3 ms and the output is RAW again.\n" +
             "There is no Recovery setting. 0 = pure RAW.")]
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
