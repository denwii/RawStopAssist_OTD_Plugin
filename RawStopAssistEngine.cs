using System;
using System.Numerics;

namespace RawStopAssistV06;

/// <summary>
/// RAW Stop Assist v0.6 — motore.
///
/// Idea: l'output è sempre la posizione RAW della penna vista con un ritardo d(t):
///     output(t) = P(t − d(t))
/// dove P è la traiettoria RAW interpolata linearmente tra i report.
///
///  • In movimento d = 0 → output identico al RAW.
///  • Quando la penna è ferma d sale fino a Dwell, a 0.5 × Strength ms per ms di sosta
///    (Strength 0–2). Da fermi un ritardo è invisibile (la penna non si sposta), quindi
///    "armare" non costa nulla e non ha scadenza: si può restare fermi quanto si vuole.
///  • Quando la penna riparte il cursore è GIÀ in ritardo di Dwell ms: resta sulla
///    posizione di stop per Dwell ms dal primo campione di ripartenza, senza bisogno
///    di riconoscere la ripartenza in anticipo.
///  • Flow aim: dopo un movimento veloce (picco > 150 mm/s), se la penna rallenta sotto
///    20% × Strength del picco senza fermarsi, d cresce a 0.5 × Strength ms per ms (l'output
///    rallenta più della penna) e alla riaccelerazione torna a 0 con lo stesso recupero
///    (l'output riaccelera più della penna). Il percorso resta quello esatto della penna.
///  • Solo il recupero (d → 0) dipende dalla conferma della ripartenza. Se la conferma
///    arriva con un report di ritardo, l'unica conseguenza è che il ritardo di Dwell ms
///    dura un report in più; non ci sono mai freeze durante il movimento.
///  • Dopo la ripartenza il ritardo resta pieno per Hold ms, poi il recupero lo
///    riduce con una curva ease-out in 2×d ÷ RecoverySpeed ms (minimo 2 report): l'output
///    segue esattamente il percorso della penna (curve comprese), solo un po' più veloce,
///    e si riaggancia al RAW senza gradini di velocità.
///
/// Significato di Dwell con report discreti: al primo report dopo la ripartenza il cursore
/// mostra la posizione che la penna aveva Dwell ms prima, interpolata linearmente tra i due
/// report. A 133 Hz (CTL-472, ~7.5 ms tra report) un Dwell di 3 ms trattiene il 40% del primo
/// spostamento; un Dwell ≥ 7.5 ms trattiene tutto il primo report, ≥ 15 ms i primi due, ecc.
/// Il valore ha quindi lo stesso significato fisico a qualunque frequenza di report.
/// </summary>
public sealed class RawStopAssistEngine
{
    public const float MaxDwellMs = 50f;
    public const float DefaultUnitsPerMm = 100f; // CTL-472: 15200 unità / 152 mm

    // --- Costanti interne (in mm e ms) ---
    private const float StillRadiusMinMm = 0.08f;   // raggio minimo in cui la penna è considerata ferma
    private const float StillRadiusMaxMm = 0.25f;   // limite superiore del raggio adattivo
    private const float NoiseToRadius = 3.5f;       // raggio = 3.5 × distanza media del noise dal centro
    private const float ConfirmRadiusFactor = 3f;   // oltre 3R la ripartenza è confermata subito
    private const float GrowingStepFactor = 0.25f;  // distanza cresciuta di ≥ 0.25R ...
    private const double GrowWindowMs = 9.0;        // ... rispetto al campione fuori più vecchio degli ultimi 9 ms
    private const double ResettleMs = 14.0;         // fuori da R da 14 ms senza conferma → nuovo centro
    private const double StillConfirmMs = 12.0;     // immobilità minima prima di armare
    public const float MaxStrength = 2f;
    public const float DefaultStrength = 1f;
    private const float GrowRatePerStrength = 0.5f; // Strength 1 → d cresce di 0.5 ms per ms, Strength 2 → 1 ms per ms
    private const float RecoveryDwellFactor = 2f;   // durata recupero = max(2 × d ÷ RecoverySpeed, 2 report)
    public const float MinRecoverySpeed = 0.25f;
    public const float MaxRecoverySpeed = 3f;
    public const float DefaultRecoverySpeed = 1f;
    public const float MaxHoldMs = 100f;
    private const float RecoveryMinReports = 2f;
    private const double MaxReportGapMs = 60.0;     // oltre: penna fuori portata → reset a RAW
    private const double HistoryMarginMs = 25.0;
    private const int HistoryCapacity = 512;

    // Calo di velocità (flow aim)
    private const float FlowMinPeakMmS = 150f;      // picco minimo perché sia un jump (esclude stream e mira lenta)
    private const float FlowCutoffPerStrength = 0.2f; // soglia del calo = 20% × Strength del picco recente
    private const float DipExitHysteresis = 1.15f;  // riaccelerazione: sopra 1.15 × soglia e in aumento
    private const float PeakDecayMs = 150f;         // memoria del picco di velocità
    private const double MaxDipMs = 150.0;          // un calo più lungo senza riaccelerare → recupero
    private const float SpeedSmoothing = 0.5f;      // EMA della velocità per report

    private enum Phase { Raw, Armed, Dip, Recovering }

    // --- Storico (ring buffer) sulla timeline filtrata ---
    private readonly double[] histT = new double[HistoryCapacity];
    private readonly Vector2[] histP = new Vector2[HistoryCapacity];
    private int histStart, histCount;

    // --- Timing ---
    private bool initialized;
    private double lastRawTime;
    private double t;            // tempo filtrato (ms)
    private float interval;      // intervallo medio tra report (ms)
    private float lastStep;      // avanzamento dell'ultimo report sulla timeline filtrata (ms)
    private float growRate = GrowRatePerStrength * DefaultStrength;
    private float strengthNow = DefaultStrength;
    private float recoverySpeedNow = DefaultRecoverySpeed;
    private float holdNow;

    // Velocità della penna (mm/s) e calo
    private float speed, prevSpeed, peakSpeed;
    private double dipStart;
    private float dipCutoff;

    // --- Stato ---
    private Phase phase;
    private float delay;         // d corrente (ms)
    private double lastTau;      // t − d dell'ultimo output: non deve mai diminuire

    // Cluster di "fermo"
    private Vector2 center;
    private int clusterCount;
    private double clusterStart;
    private double lastInsideTime;
    private int outsideCount;

    // Escursione fuori da R (tempo, distanza dal centro) per la conferma della ripartenza
    private readonly double[] excursionT = new double[256];
    private readonly float[] excursionD = new float[256];
    private int excursionCount;
    private double excursionStart;
    private float noise;         // distanza media dal centro mentre fermi (unità tablet)

    // Recupero
    private float recoveryFrom;
    private double recoveryStart;
    private float recoveryLength;

    public float UnitsPerMm { get; set; } = DefaultUnitsPerMm;

    /// <summary>Usati dai test/simulazioni.</summary>
    public int ArmCount { get; private set; }
    public int RestartCount { get; private set; }
    public int DipCount { get; private set; }
    public float CurrentDelayMs => delay;

    public void Reset(Vector2 input, double nowMs)
    {
        initialized = true;
        lastRawTime = nowMs;
        t = nowMs;
        if (!(interval > 0f))
            interval = 7.5f;
        phase = Phase.Raw;
        delay = 0f;
        lastTau = nowMs;
        speed = prevSpeed = peakSpeed = 0f;
        histStart = 0;
        histCount = 0;
        Push(t, input);
        StartCluster(input);
        if (!(noise > 0f))
            noise = StillRadiusMinMm * UnitsPerMm / NoiseToRadius;
    }

    public Vector2 Process(Vector2 input, double nowMs, float dwellMs) =>
        Process(input, nowMs, dwellMs, DefaultStrength, DefaultRecoverySpeed);

    public Vector2 Process(Vector2 input, double nowMs, float dwellMs, float strength) =>
        Process(input, nowMs, dwellMs, strength, DefaultRecoverySpeed);

    // Senza Hold esplicito: Hold = Dwell, cioè il comportamento della prima v0.6 dopo gli stop.
    public Vector2 Process(Vector2 input, double nowMs, float dwellMs, float strength, float recoverySpeed) =>
        Process(input, nowMs, dwellMs, strength, recoverySpeed, dwellMs);

    /// <param name="strength">
    /// 0–2: quanto il cursore rallenta attorno ai cerchi (flow) e quanto in fretta si prepara il
    /// trattenimento durante uno stop. 0 = RAW.
    /// </param>
    /// <param name="recoverySpeed">
    /// 0.25–3: velocità del recupero. Durata = 2 × ritardo ÷ recoverySpeed (minimo 2 report);
    /// velocità massima dell'output = (1 + recoverySpeed) × velocità della penna ritardata.
    /// </param>
    /// <param name="holdMs">
    /// 0–100 ms: per quanto il ritardo resta pieno dopo la ripartenza (dall'ultimo campione fermo
    /// dopo uno stop, dalla riaccelerazione in flow) prima che parta il recupero.
    /// </param>
    public Vector2 Process(Vector2 input, double nowMs, float dwellMs, float strength, float recoverySpeed,
        float holdMs)
    {
        holdNow = float.IsFinite(holdMs) ? Math.Clamp(holdMs, 0f, MaxHoldMs) : 0f;
        recoverySpeedNow = float.IsFinite(recoverySpeed)
            ? Math.Clamp(recoverySpeed, MinRecoverySpeed, MaxRecoverySpeed)
            : DefaultRecoverySpeed;
        if (!float.IsFinite(strength) || strength <= 0f)
            dwellMs = 0f; // Strength 0: il filtro non si prepara mai → RAW
        strengthNow = float.IsFinite(strength) ? Math.Clamp(strength, 0f, MaxStrength) : 0f;
        growRate = GrowRatePerStrength * strengthNow;

        if (!float.IsFinite(input.X) || !float.IsFinite(input.Y) || !double.IsFinite(nowMs))
            return initialized ? Sample(lastTau) : input;

        // Dwell 0 → RAW puro.
        if (!float.IsFinite(dwellMs) || dwellMs <= 0f)
        {
            initialized = false;
            return input;
        }
        dwellMs = MathF.Min(dwellMs, MaxDwellMs);

        double rawDt = nowMs - lastRawTime;
        if (!initialized || rawDt > MaxReportGapMs || rawDt < 0.0)
        {
            Reset(input, nowMs);
            return input;
        }
        lastRawTime = nowMs;
        Vector2 previous = histP[Index(histCount - 1)];
        AdvanceClock(nowMs, rawDt);
        Push(t, input);
        UpdateSpeed(input, previous);

        float radius = Radius();
        float dist = Vector2.Distance(input, center);
        bool inside = dist <= radius;

        switch (phase)
        {
            case Phase.Raw:
                if (inside)
                {
                    AddToCluster(input, dist);
                    if (clusterCount >= 2 && t - clusterStart >= StillConfirmMs)
                    {
                        phase = Phase.Armed;
                        outsideCount = 0;
                        ArmCount++;
                        GrowDelay(dwellMs);
                    }
                }
                else
                {
                    StartCluster(input);
                }
                break;

            case Phase.Armed:
                if (inside)
                {
                    outsideCount = 0;
                    AddToCluster(input, dist);
                    noise += 0.05f * (dist - noise);
                    GrowDelay(dwellMs);
                }
                else
                {
                    if (outsideCount == 0)
                    {
                        excursionStart = t;
                        excursionCount = 0;
                    }
                    outsideCount++;
                    RecordExcursion(t, dist);

                    // Soglie in ms, non in report: stesso comportamento a 133 Hz e a 1000 Hz.
                    bool far = dist > radius * ConfirmRadiusFactor;
                    bool growing = OldestInWindow(GrowWindowMs, out float before)
                        && dist > before + radius * GrowingStepFactor;

                    if (far || growing)
                    {
                        BeginRecovery();
                    }
                    else if (t - excursionStart >= ResettleMs)
                    {
                        // Piccolo spostamento intenzionale e di nuovo fermo: resta armato attorno
                        // al nuovo punto. d non cresce finché non c'è di nuovo un tratto fermo.
                        StartCluster(input);
                    }
                }
                break;

            case Phase.Dip:
                // Il calo può diventare uno stop vero: in quel caso si passa ad Armed tenendo il ritardo.
                if (inside)
                {
                    AddToCluster(input, dist);
                    if (clusterCount >= 2 && t - clusterStart >= StillConfirmMs)
                    {
                        phase = Phase.Armed;
                        outsideCount = 0;
                        ArmCount++;
                        GrowDelay(dwellMs);
                        break;
                    }
                }
                else
                {
                    StartCluster(input);
                }

                // Durante il calo l'output scorre il percorso a (1 − growRate) della velocità della penna.
                delay = MathF.Min(dwellMs, delay + growRate * lastStep);

                bool reaccelerating = speed > dipCutoff * DipExitHysteresis && speed > prevSpeed;
                if (reaccelerating || t - dipStart > MaxDipMs)
                    BeginRecovery(immediate: true);
                break;

            case Phase.Recovering:
                UpdateRecovery();
                break;
        }

        // Calo di velocità dopo un movimento veloce (flow aim): la penna non si ferma, ma rallenta
        // sotto FlowCutoffPerStrength × Strength del picco recente e poi riaccelera.
        if (phase == Phase.Raw && growRate > 0f && peakSpeed >= FlowMinPeakMmS
            && speed < prevSpeed && speed < FlowCutoffPerStrength * strengthNow * peakSpeed)
        {
            phase = Phase.Dip;
            dipStart = t;
            dipCutoff = FlowCutoffPerStrength * strengthNow * peakSpeed;
            DipCount++;
            delay = MathF.Min(dwellMs, growRate * lastStep);
        }

        if (phase == Phase.Raw)
        {
            delay = 0f;
            lastTau = t;
            return input;
        }

        double tau = Math.Max(lastTau, t - delay);
        delay = (float)(t - tau);
        lastTau = tau;
        return Sample(tau);
    }

    // d cresce solo mentre la penna è dentro il cluster, mai oltre l'inizio del cluster
    // (l'output resta entro R dalla penna e non torna indietro lungo l'avvicinamento), a
    // growRate ms per ms. Con Strength 1 (0.5 ms/ms) l'output durante la preparazione rallenta
    // ma non si congela; con Strength 2 (1 ms/ms) si ferma, sempre entro R dalla penna.
    private void GrowDelay(float dwellMs)
    {
        float allowed = (float)Math.Min(Math.Min(dwellMs, t - clusterStart), delay + growRate * lastStep);
        if (allowed > delay)
            delay = allowed;
    }

    private void BeginRecovery(bool immediate = false)
    {
        RestartCount++;
        if (delay <= 0.01f)
        {
            phase = Phase.Raw;
            StartCluster(histP[Index(histCount - 1)]);
            return;
        }

        phase = Phase.Recovering;
        recoveryFrom = delay;
        // Il ritardo resta pieno per Hold ms: dopo uno stop dall'ultimo campione fermo (ma non prima
        // della conferma della ripartenza), dopo un calo (flow) dalla riaccelerazione.
        recoveryStart = immediate ? t + holdNow : Math.Max(t, lastInsideTime + holdNow);
        recoveryLength = MathF.Max(RecoveryDwellFactor * delay / recoverySpeedNow, RecoveryMinReports * interval);
        UpdateRecovery();
    }

    private void UpdateRecovery()
    {
        float u = (float)((t - recoveryStart) / recoveryLength);
        if (u <= 0f)
        {
            delay = recoveryFrom;
        }
        else if (u >= 1f)
        {
            phase = Phase.Raw;
            delay = 0f;
            StartCluster(histP[Index(histCount - 1)]);
        }
        else
        {
            // Ease-out quadratico: d = d0·(1−u)². Recupera di più all'inizio, quando il tratto
            // rivisto è ancora lento, e si riaggancia al RAW con d' = 0 (nessun gradino di
            // velocità nel punto in cui la penna è più veloce). Scelto per simulazione contro
            // lineare/smoothstep/smootherstep: picco di velocità e di accelerazione più bassi.
            float r = 1f - u;
            delay = recoveryFrom * r * r;
        }
    }

    // Velocità della penna in mm/s (EMA per report) e picco recente con decadimento esponenziale.
    private void UpdateSpeed(Vector2 input, Vector2 previous)
    {
        float instant = lastStep > 0f ? Vector2.Distance(input, previous) / UnitsPerMm / (lastStep * 0.001f) : speed;
        prevSpeed = speed;
        speed += SpeedSmoothing * (instant - speed);
        peakSpeed = MathF.Max(speed, peakSpeed * MathF.Exp(-lastStep / PeakDecayMs));
    }

    private void StartCluster(Vector2 p)
    {
        center = p;
        clusterCount = 1;
        clusterStart = t;
        lastInsideTime = t;
        outsideCount = 0;
        excursionCount = 0;
    }

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

    // Distanza dal centro del campione fuori più vecchio negli ultimi windowMs (escluso l'attuale).
    // A 133 Hz è il report precedente; a 1000 Hz il confronto copre lo stesso intervallo di tempo.
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

    private void AddToCluster(Vector2 p, float dist)
    {
        clusterCount++;
        center += (p - center) / Math.Min(clusterCount, 16);
        lastInsideTime = t;
    }

    private float Radius()
    {
        float r = noise * NoiseToRadius;
        return Math.Clamp(r, StillRadiusMinMm * UnitsPerMm, StillRadiusMaxMm * UnitsPerMm);
    }

    // Timeline filtrata: assorbe il jitter dei timestamp (USB/scheduling) senza perdere
    // la sincronia a lungo termine con il clock reale.
    private void AdvanceClock(double nowMs, double rawDt)
    {
        if (rawDt >= 0.5 && rawDt <= 40.0)
            interval += 0.05f * ((float)rawDt - interval);

        double predicted = t + interval;
        double next = predicted + 0.2 * (nowMs - predicted);
        double clamped = Math.Clamp(next, t + 0.3 * interval, t + 3.0 * interval);
        lastStep = (float)(clamped - t);
        t = clamped;
    }

    private void Push(double time, Vector2 p)
    {
        if (histCount == HistoryCapacity)
        {
            histStart = (histStart + 1) % HistoryCapacity;
            histCount--;
        }
        int i = Index(histCount);
        histT[i] = time;
        histP[i] = p;
        histCount++;

        double keep = time - MaxDwellMs - HistoryMarginMs;
        while (histCount > 2 && histT[Index(1)] < keep)
        {
            histStart = (histStart + 1) % HistoryCapacity;
            histCount--;
        }
    }

    private int Index(int k) => (histStart + k) % HistoryCapacity;

    private Vector2 Sample(double tau)
    {
        int last = histCount - 1;
        if (tau >= histT[Index(last)])
            return histP[Index(last)];

        for (int k = last - 1; k >= 0; k--)
        {
            double t0 = histT[Index(k)];
            if (t0 <= tau)
            {
                double t1 = histT[Index(k + 1)];
                float a = t1 > t0 ? (float)((tau - t0) / (t1 - t0)) : 1f;
                return Vector2.Lerp(histP[Index(k)], histP[Index(k + 1)], a);
            }
        }
        return histP[Index(0)];
    }
}
