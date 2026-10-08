using System;
using System.Numerics;

namespace RawStopAssistV09;

/// <summary>
/// RAW Stop Assist v0.9 — restart-only hold.
///
/// Design goals:
///  • Normal movement is exactly RAW.
///  • A detected stop only ARMS the filter; standing still never adds delay.
///  • The hold starts on the first excursion away from an armed stop and lasts at most Hold ms.
///  • While it lasts, the cursor follows the pen through Devocub's antichatter weighting, so it
///    sticks while the pen is still close and lets go once the pen has pulled away.
///  • Strength sets how far the pen has to pull away: higher Strength always holds more.
///  • When Hold expires, or the pen comes back to rest, the remaining lag collapses within a few
///    milliseconds and the output is bit-exact RAW again. There is no recovery setting.
///
/// The output during a hold report is:
///     output = input - lag
///     lag    = (input - previousOutput) * (1 - weightPerMs)^dt
///     weightPerMs = base / (1 + (knee / lagMm)^3),   knee = Strength * 0.1 mm
///
/// This is Devocub's "weight / (pow(distance, -strength) * multiplier + offsetY)" with the exponent
/// fixed at its default 3 and Strength acting on the knee distance, expressed per millisecond so the
/// result does not depend on the report rate.
/// </summary>
public sealed class RawStopAssistEngine
{
    public const float DefaultUnitsPerMm = 100f; // CTL-472: 15200 units / 152 mm

    // --- Stop/restart detector ---
    private const float StillRadiusMinMm = 0.08f;
    private const float StillRadiusMaxMm = 0.25f;
    private const float NoiseToRadius = 3.5f;
    private const float ConfirmRadiusFactor = 3f;
    private const float GrowingStepFactor = 0.25f;
    private const double GrowWindowMs = 9.0;
    private const double ResettleMs = 14.0;
    private const double StillConfirmMs = 12.0;
    private const double MaxReportGapMs = 60.0;

    // A stop also needs the newer half of the confirmation window to sit on top of the older half.
    // Slow steady motion (slider follow) fits inside the radius but fails this.
    private const float DriftFactor = 0.5f;

    // Time constants instead of per-report factors: the detector must behave the same on the
    // tablet's native rate and behind a 1000 Hz interpolating filter.
    private const float AnchorTauMs = 16f;
    private const float NoiseTauMs = 100f;

    // Noise is read from second differences, which ignore drift. For 2D jitter their mean length is
    // 3.07 sigma, while the mean distance from the rest point (what the radius is scaled from) is
    // 1.25 sigma.
    private const float SecondDiffToNoise = 0.41f;

    private const int HistorySize = 256;

    // --- User-facing settings ---
    public const float MaxStrength = 20f;
    public const float DefaultStrength = 5f;
    public const float MaxHoldMs = 50f;
    public const float DefaultHoldMs = 8f;

    // --- Fixed hold tuning ---
    // Strength 10 puts the knee at 1 mm, where Devocub's own antichatter has it.
    private const float StrengthToKneeMm = 0.1f;

    // Devocub's default antichatter strength.
    private const float HoldPower = 3f;

    // Devocub's base weight at Latency 1 ms / 1000 Hz: 90% of the gap per millisecond.
    private const float BaseWeightPerMs = 0.9f;

    // Remaining lag below which a release ends and the output is RAW again.
    private const float ReleaseSnapMm = 0.005f;

    private enum Phase { Raw, Armed }

    // --- Timing ---
    private bool initialized;
    private double lastRawTime;
    private double t;
    private float interval;
    private float lastStep;
    private bool intervalReady;

    // --- RAW motion ---
    private Vector2 previousRaw;
    private readonly double[] historyT = new double[HistorySize];
    private readonly Vector2[] historyP = new Vector2[HistorySize];
    private int historyHead;
    private int historyCount;

    // --- Stop detector state ---
    private Phase phase;
    private Vector2 anchor;
    private int outsideCount;

    private readonly double[] excursionT = new double[256];
    private readonly float[] excursionD = new float[256];
    private int excursionCount;
    private double excursionStart;
    private float noise;

    // --- Restart-only hold ---
    private bool holdActive;
    private bool releasing;
    private double holdStart;
    private Vector2 held;

    public float UnitsPerMm { get; set; } = DefaultUnitsPerMm;

    // Diagnostics for simulations/debugging. They do not affect output.
    public int ArmCount { get; private set; }
    public int RestartCount { get; private set; }
    public bool IsHolding => holdActive;
    public float CurrentLagMm { get; private set; }

    public void Reset(Vector2 input, double nowMs)
    {
        initialized = true;
        lastRawTime = nowMs;
        t = nowMs;
        if (!(interval > 0f))
            interval = 1f;
        lastStep = interval;
        intervalReady = false;

        previousRaw = input;
        historyCount = 0;
        PushHistory(input);

        phase = Phase.Raw;
        outsideCount = 0;
        excursionCount = 0;
        holdActive = false;
        releasing = false;
        CurrentLagMm = 0f;

        if (!(noise > 0f))
            noise = StillRadiusMinMm * UnitsPerMm / NoiseToRadius;
    }

    /// <summary>
    /// Process one RAW tablet position.
    /// Strength is how firmly the cursor is held when leaving a stop: the pen has to pull about
    /// Strength × 0.1 mm away before the cursor follows it. Strength 0 = bypass.
    /// Hold is how long that lasts, in milliseconds, from the first movement away. Hold 0 = bypass.
    /// </summary>
    public Vector2 Process(Vector2 input, double nowMs, float strength, float holdMs)
    {
        if (!float.IsFinite(input.X) || !float.IsFinite(input.Y) || !double.IsFinite(nowMs))
            return input;

        strength = float.IsFinite(strength) ? Math.Clamp(strength, 0f, MaxStrength) : 0f;
        holdMs = float.IsFinite(holdMs) ? Math.Clamp(holdMs, 0f, MaxHoldMs) : 0f;

        // Either zero means true RAW bypass. Reset state so re-enabling starts cleanly.
        if (strength <= 0f || holdMs <= 0f || !(UnitsPerMm > 0f))
        {
            initialized = false;
            CurrentLagMm = 0f;
            return input;
        }

        double rawDt = initialized ? nowMs - lastRawTime : 0.0;
        if (!initialized || rawDt > MaxReportGapMs || rawDt < 0.0)
        {
            Reset(input, nowMs);
            return input;
        }

        lastRawTime = nowMs;
        AdvanceClock(nowMs, rawDt);
        PushHistory(input);

        bool windowReady = MeasureWindow(out Vector2 centroid, out float spread, out float drift);
        if (windowReady && spread <= StillRadiusMaxMm * UnitsPerMm)
            UpdateNoise();

        float radius = Radius();

        switch (phase)
        {
            case Phase.Raw:
                if (windowReady && spread <= radius && drift <= radius * DriftFactor)
                {
                    phase = Phase.Armed;
                    anchor = centroid;
                    outsideCount = 0;
                    ArmCount++;

                    // A hold still running from the previous restart must not outlive the new stop.
                    ReleaseHold();
                }
                break;

            case Phase.Armed:
                float dist = Vector2.Distance(input, anchor);
                if (dist <= radius)
                {
                    // An excursion that came back inside was noise / a tiny correction, not a restart.
                    if (outsideCount > 0)
                        ReleaseHold();

                    outsideCount = 0;
                    anchor += (input - anchor) * TimeBlend(AnchorTauMs);
                }
                else
                {
                    if (outsideCount == 0)
                    {
                        excursionStart = t;
                        excursionCount = 0;
                        BeginHold(); // first actual movement away from an armed stop
                    }

                    outsideCount++;
                    RecordExcursion(t, dist);

                    // Same v0.7 restart confirmation logic. It does not control when the hold
                    // begins; it only tells the detector that this was a real departure.
                    bool far = dist > radius * ConfirmRadiusFactor;
                    bool growing = OldestInWindow(GrowWindowMs, out float before)
                        && dist > before + radius * GrowingStepFactor;

                    if (far || growing)
                    {
                        RestartCount++;
                        phase = Phase.Raw;
                    }
                    else if (t - excursionStart >= ResettleMs)
                    {
                        // Small intentional reposition followed by another stop. Keep the detector
                        // armed around the new point, but do not carry the previous hold with it.
                        ReleaseHold();
                        anchor = input;
                        outsideCount = 0;
                    }
                }
                break;
        }

        Vector2 output = ApplyHold(input, strength, holdMs);
        previousRaw = input;
        return output;
    }

    private void BeginHold()
    {
        // The output has been RAW up to the previous report, unless a release is still running.
        if (!holdActive)
            held = previousRaw;

        holdActive = true;
        releasing = false;
        holdStart = t;
    }

    private void ReleaseHold()
    {
        if (holdActive)
            releasing = true;
    }

    private Vector2 ApplyHold(Vector2 input, float strength, float holdMs)
    {
        CurrentLagMm = 0f;

        if (!holdActive)
            return input;

        if (!releasing && t - holdStart >= holdMs)
            releasing = true;

        float dt = MathF.Max(lastStep, 0.05f);
        Vector2 lag;

        if (releasing)
        {
            // Shrink the lag that is left instead of chasing the pen, so the release takes the
            // same few milliseconds however fast the pen is moving by now.
            lag = (previousRaw - held) * MathF.Pow(1f - BaseWeightPerMs, dt);
            if (lag.Length() <= ReleaseSnapMm * UnitsPerMm)
            {
                holdActive = false;
                return input;
            }
        }
        else
        {
            lag = input - held;
            float lagMm = lag.Length() / UnitsPerMm;
            if (lagMm > 0f)
            {
                float knee = strength * StrengthToKneeMm;
                float weightPerMs = BaseWeightPerMs / (1f + MathF.Pow(knee / lagMm, HoldPower));
                lag *= MathF.Pow(1f - weightPerMs, dt);
            }
        }

        held = input - lag;
        CurrentLagMm = lag.Length() / UnitsPerMm;
        return held;
    }

    private void PushHistory(Vector2 p)
    {
        historyHead = (historyHead + 1) % HistorySize;
        historyT[historyHead] = t;
        historyP[historyHead] = p;
        if (historyCount < HistorySize)
            historyCount++;
    }

    // age 0 is the newest report
    private int HistoryIndex(int age) => (historyHead - age + HistorySize) % HistorySize;

    /// <summary>
    /// Measures the reports of the last StillConfirmMs: how far they spread around their centroid
    /// and how far the newer half has drifted from the older half. False until they span that long.
    /// </summary>
    private bool MeasureWindow(out Vector2 centroid, out float spread, out float drift)
    {
        centroid = default;
        spread = 0f;
        drift = 0f;

        int n = 0;
        bool spans = false;
        Vector2 sum = Vector2.Zero;
        while (n < historyCount && !spans)
        {
            int i = HistoryIndex(n);
            sum += historyP[i];
            spans = t - historyT[i] >= StillConfirmMs;
            n++;
        }

        if (!spans || n < 3)
            return false;

        centroid = sum / n;

        int half = n / 2;
        Vector2 newer = Vector2.Zero;
        Vector2 older = Vector2.Zero;
        for (int age = 0; age < n; age++)
        {
            Vector2 p = historyP[HistoryIndex(age)];
            spread = MathF.Max(spread, Vector2.Distance(p, centroid));

            if (age < half)
                newer += p;
            else if (age >= n - half)
                older += p;
        }

        drift = Vector2.Distance(newer, older) / half;
        return true;
    }

    private void UpdateNoise()
    {
        Vector2 p0 = historyP[HistoryIndex(0)];
        Vector2 p1 = historyP[HistoryIndex(1)];
        Vector2 p2 = historyP[HistoryIndex(2)];

        float residual = (p0 - 2f * p1 + p2).Length() * SecondDiffToNoise;
        noise += (residual - noise) * TimeBlend(NoiseTauMs);
    }

    private float TimeBlend(float tauMs) => 1f - MathF.Exp(-lastStep / tauMs);

    private void RecordExcursion(double time, float dist)
    {
        if (excursionCount == excursionT.Length)
        {
            Array.Copy(excursionT, 1, excursionT, 0, excursionCount - 1);
            Array.Copy(excursionD, 1, excursionD, 0, excursionCount - 1);
            excursionCount--;
        }

        excursionT[excursionCount] = time;
        excursionD[excursionCount] = dist;
        excursionCount++;
    }

    private bool OldestInWindow(double windowMs, out float dist)
    {
        for (int i = 0; i < excursionCount - 1; i++)
        {
            if (excursionT[i] >= t - windowMs)
            {
                dist = excursionD[i];
                return true;
            }
        }

        dist = 0f;
        return false;
    }

    private float Radius()
    {
        float r = noise * NoiseToRadius;
        return Math.Clamp(r, StillRadiusMinMm * UnitsPerMm, StillRadiusMaxMm * UnitsPerMm);
    }

    // Preserved v0.7 timing smoother. It only times the detector and the hold;
    // it never delays or resamples the cursor position.
    private void AdvanceClock(double nowMs, double rawDt)
    {
        if (rawDt >= 0.5 && rawDt <= 40.0)
        {
            if (!intervalReady)
            {
                interval = (float)rawDt;
                intervalReady = true;
            }
            else
            {
                interval += 0.05f * ((float)rawDt - interval);
            }
        }

        double predicted = t + interval;
        double next = predicted + 0.2 * (nowMs - predicted);
        double clamped = Math.Clamp(next, t + 0.3 * interval, t + 3.0 * interval);
        lastStep = (float)(clamped - t);
        t = clamped;
    }
}
