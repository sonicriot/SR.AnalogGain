using System;
using System.Runtime.CompilerServices;
using NPlug;

namespace SR.AnalogGain;

public class AnalogGainProcessor : AudioProcessor<AnalogGainModel>
{
    public static readonly Guid ClassId = new("0da5855d-c9f8-49a3-b422-30a06112b040");

    public AnalogGainProcessor() : base(AudioSampleSizeSupport.Float32) { }

    public override Guid ControllerClassId => AnalogGainController.ClassId;

    // --- Per-channel state ---
    private RmsDetector[] _rms = Array.Empty<RmsDetector>();
    private Biquad[] _preLF = Array.Empty<Biquad>();
    private Biquad[] _postLF = Array.Empty<Biquad>();
    private Biquad[] _postHS = Array.Empty<Biquad>();
    private Biquad[] _postHS2 = Array.Empty<Biquad>();
    private OnePoleLP[] _lpHfTamer = Array.Empty<OnePoleLP>();   // very gentle HF tamer
    private DcBlock[] _dc = Array.Empty<DcBlock>();
    private OnePoleLP[] _lozLP = Array.Empty<OnePoleLP>();       // LO-Z lowpass (6 kHz)
    private float[] _lozBlendZ = Array.Empty<float>();
    private float[] _lozPadZ = Array.Empty<float>();
    private float[] _gainZ = Array.Empty<float>();               // smoothed gain
    private float[] _adaaPrev = Array.Empty<float>();            // ADAA state
    private float[] _outputZ = Array.Empty<float>();
    private float[] _padZ = Array.Empty<float>();
    private float[] _phaseZ = Array.Empty<float>();              // smoothed polarity (+1 ↔ -1)
    private OnePoleHP[] _hpf1 = Array.Empty<OnePoleHP>();        // 6 dB/oct
    private Biquad[] _hpf2 = Array.Empty<Biquad>();              // 12 dB/oct
    private float[] _hpfMixZ = Array.Empty<float>();             // ramp 0..1
    private float[] _satMixZ = Array.Empty<float>();

    private double _fs = 48000.0;
    private int _configuredChannels = -1;
    private double _configuredFs = -1.0;

    // Fixed mild coloration (pre/post shelves + subtle HF tilt)
    private const double PreLF_Freq = 140.0;   // Hz
    private const double PreLF_Gain = +2.0;    // dB
    private const double PostLF_Gain = -1.7;   // dB (compensates PreLF)
    private const double PostHS_Freq = 18000.0; // Hz
    private const double PostHS_Gain = -1.4;   // dB (subtle tilt)

    private const float AntiDenorm = 1e-30f; // ~ -300 dBFS

    protected override bool Initialize(AudioHostApplication host)
    {
        AddAudioInput("AudioInput", SpeakerArrangement.SpeakerStereo);
        AddAudioOutput("AudioOutput", SpeakerArrangement.SpeakerStereo);
        return true;
    }

    protected override void OnActivate(bool isActive)
    {
        if (!isActive) return;
        _fs = ProcessSetupData.SampleRate;
        _configuredFs = -1.0;
        _configuredChannels = -1;
        EnsureChannelState(dataChannelCount: null);
    }

    private void EnsureChannelState(int? dataChannelCount)
    {
        int channels = dataChannelCount ?? 2;

        bool sizeMismatch =
            _rms.Length != channels ||
            _preLF.Length != channels ||
            _postLF.Length != channels ||
            _postHS.Length != channels ||
            _postHS2.Length != channels ||
            _lpHfTamer.Length != channels ||
            _dc.Length != channels ||
            _lozLP.Length != channels ||
            _lozBlendZ.Length != channels ||
            _lozPadZ.Length != channels ||
            _gainZ.Length != channels ||
            _adaaPrev.Length != channels ||
            _outputZ.Length != channels ||
            _padZ.Length != channels ||
            _phaseZ.Length != channels ||
            _hpf1.Length != channels ||
            _hpf2.Length != channels ||
            _hpfMixZ.Length != channels ||
            _satMixZ.Length != channels;   // NEW

        if (sizeMismatch)
        {
            _rms = new RmsDetector[channels];
            _preLF = new Biquad[channels];
            _postLF = new Biquad[channels];
            _postHS = new Biquad[channels];
            _postHS2 = new Biquad[channels];
            _lpHfTamer = new OnePoleLP[channels];
            _dc = new DcBlock[channels];
            _lozLP = new OnePoleLP[channels];
            _lozBlendZ = new float[channels];
            _lozPadZ = new float[channels];
            _gainZ = new float[channels];
            _adaaPrev = new float[channels];
            _outputZ = new float[channels];
            _padZ = new float[channels];
            _phaseZ = new float[channels];
            _hpf1 = new OnePoleHP[channels];
            _hpf2 = new Biquad[channels];
            _hpfMixZ = new float[channels];
            _satMixZ = new float[channels]; // NEW
        }

        if (sizeMismatch || _configuredFs != _fs || _configuredChannels != channels)
        {
            const double HPF_FREQ = 80.0;   // Neve common setting; 18 dB/oct overall
            const double HPF_Q = 0.707;     // biquad stage for Butterworth-ish section

            for (int ch = 0; ch < channels; ch++)
            {
                _rms[ch].Setup(_fs, attackMs: 5.0, releaseMs: 60.0);
                _preLF[ch].SetLowShelf(_fs, PreLF_Freq, PreLF_Gain);
                _postLF[ch].SetLowShelf(_fs, PreLF_Freq, PostLF_Gain);
                _postHS[ch].SetHighShelf(_fs, PostHS_Freq, PostHS_Gain, 0.7);
                _postHS2[ch].SetHighShelf(_fs, 4000.0, 0.6, 0.5);
                _lpHfTamer[ch].Setup(_fs, cutoffHz: 22000.0);
                _lozLP[ch].Setup(_fs, cutoffHz: 6000.0);
                _dc[ch].Setup(_fs, 8.0);
                _hpf1[ch].Setup(_fs, HPF_FREQ);
                _hpf2[ch].SetHighPass(_fs, HPF_FREQ, HPF_Q);
            }
            _configuredFs = _fs;
            _configuredChannels = channels;
        }
    }

    protected override void ProcessMain(in AudioProcessData data)
    {
        var inputBus = data.Input[0];
        var outputBus = data.Output[0];

        // Track host sample rate changes
        double sampleRate = ProcessSetupData.SampleRate;
        if (sampleRate > 1000.0 && sampleRate != _fs)
        {
            _fs = sampleRate;
            _configuredFs = -1.0;
        }

        EnsureChannelState(inputBus.ChannelCount);

        // Gain mapping: normalized [0..1] → dB → linear
        const double minDb = -60.0;
        const double maxDb = +12.0;
        double normalized = Model.Gain.NormalizedValue;
        double gainDb = minDb + normalized * (maxDb - minDb);
        float gain = DbToLin((float)gainDb);

        // Output mapping: normalized [0..1] → dB → linear
        const double outMinDb = -24.0;
        const double outMaxDb = +12.0;
        double outNorm = Model.Output.NormalizedValue;
        double outDb = outMinDb + outNorm * (outMaxDb - outMinDb);
        float outTarget = DbToLin((float)outDb);

        // --- LO-Z (block-scope) ---
        bool lozOn = Model.LoZ.Value;
        const float kLoZPadDb = -1.0f;
        const float kLoZShelfDb = -1.5f;
        float lozPadTargetBlock = lozOn ? DbToLin(kLoZPadDb) : 1.0f;
        float lozBlendTargetBlock = lozOn ? (1.0f - DbToLin(kLoZShelfDb)) : 0.0f;

        // --- Other switches ---
        bool padOn = Model.Pad.Value;
        float padTargetBlock = padOn ? 0.1f : 1.0f;

        bool phaseOn = Model.Phase.Value;
        float phaseTargetBlock = phaseOn ? -1.0f : 1.0f;

        bool hpfOn = Model.Hpf.Value;
        float hpfTargetBlock = hpfOn ? 1.0f : 0.0f;

        // --- SAT (block-scope target) ---
        bool satOn = Model.Sat.Value;                // assumes the model has a Color parameter
        float satTargetBlock = satOn ? 1.0f : 0.0f;  // 0=off constants, 1=Color constants

        int channels = inputBus.ChannelCount;
        for (int ch = 0; ch < channels; ch++)
        {
            var input = inputBus.GetChannelSpanAsFloat32(ProcessSetupData, data, ch);
            var output = outputBus.GetChannelSpanAsFloat32(ProcessSetupData, data, ch);

            ref var rms = ref _rms[ch];
            ref var preLF = ref _preLF[ch];
            ref var postLF = ref _postLF[ch];
            ref var postHS = ref _postHS[ch];
            ref var postHS2 = ref _postHS2[ch];
            ref var lpHfTamer = ref _lpHfTamer[ch];
            ref var dc = ref _dc[ch];

            // Seed and ramp PAD
            if (_padZ[ch] == 0f) _padZ[ch] = padTargetBlock;
            float padNow = _padZ[ch];
            float padInc = (padTargetBlock - padNow) / Math.Max(1, data.SampleCount);

            // Seed and ramp PHASE
            if (_phaseZ[ch] == 0f) _phaseZ[ch] = phaseTargetBlock; // avoid mid-block fade on first use
            float phaseNow = _phaseZ[ch];
            float phaseInc = (phaseTargetBlock - phaseNow) / Math.Max(1, data.SampleCount);

            // initialize smoothed gain on first buffer
            if (_gainZ[ch] == 0f) _gainZ[ch] = gain;

            // HPF ramp
            if (_hpfMixZ[ch] == 0f && hpfOn) _hpfMixZ[ch] = 1.0f;
            float hpfMix = _hpfMixZ[ch];
            float hpfInc = (hpfTargetBlock - hpfMix) / Math.Max(1, data.SampleCount);

            float g = _gainZ[ch];
            float gInc = (gain - g) / Math.Max(1, data.SampleCount);

            // Output trim ramp
            if (_outputZ[ch] == 0f) _outputZ[ch] = outTarget;
            float outNow = _outputZ[ch];
            float outInc = (outTarget - outNow) / Math.Max(1, data.SampleCount);

            // --- LO-Z precompute (per channel, using block targets) ---
            float lozPadTarget = lozPadTargetBlock;
            float lozBlendTarget = lozBlendTargetBlock;
            // Seed ramps if this channel has no prior state and LO-Z starts ON
            if (_lozBlendZ[ch] == 0f && _lozPadZ[ch] == 0f && lozOn)
            {
                _lozBlendZ[ch] = lozBlendTarget;
                _lozPadZ[ch] = lozPadTarget;
            }
            float lozBlend = _lozBlendZ[ch];
            float lozPad = _lozPadZ[ch];
            float invCount = 1.0f / Math.Max(1, data.SampleCount);
            float lozBlendInc = (lozBlendTarget - lozBlend) * invCount;
            float lozPadInc = (lozPadTarget - lozPad) * invCount;

            // --- SAT per-channel ramp seed ---
            if (_satMixZ[ch] == 0f && satOn) _satMixZ[ch] = 1.0f; // seed if we start with ON
            float satMix = _satMixZ[ch];
            float satInc = (satTargetBlock - satMix) / Math.Max(1, data.SampleCount);

            for (int i = 0; i < data.SampleCount; i++)
            {
                // 1) Global PAD (pre-input), then smoothed linear gain
                padNow += padInc;

                // 1) Linear gain (smoothed)
                g += gInc;

                // ramp LO-Z params
                lozBlend += lozBlendInc;
                lozPad += lozPadInc;
                lozBlend = MathF.Min(1f, MathF.Max(0f, lozBlend));
                lozPad = MathF.Min(1f, MathF.Max(0f, lozPad));

                // ramp SAT mix
                satMix += satInc;
                satMix = MathF.Min(1f, MathF.Max(0f, satMix));

                float x = input[i] * padNow * g;

                // polarity flip as a short ramp to avoid clicks
                phaseNow += phaseInc;
                x *= phaseNow;

                // LO-Z stage: tiny pad + gentle HF tilt (blend with 6 kHz LP)
                if (lozOn || lozBlend > 1e-6f)
                {
                    x *= lozPad;
                    float xLp = _lozLP[ch].Process(x);
                    x = (1.0f - lozBlend) * x + lozBlend * xLp;
                }

                // HPF stage (18 dB/oct via 1st + 2nd)
                hpfMix += hpfInc;
                float xHP = _hpf2[ch].Process(_hpf1[ch].Process(x));
                x = (1.0f - hpfMix) * x + hpfMix * xHP;

                // --- 2) RMS envelope → drive control (s in 0..1) ---
                float e = rms.Process(x);

                // SAT-lerped knees
                float kneeLo = Lerp(0.200f, 0.10f, satMix);   // -14 dBFS → -20 dBFS
                float kneeHi = Lerp(0.750f, 0.7079f, satMix);   //  -2.5 dBFS →  -3 dBFS

                float s = SmoothStep(kneeLo, kneeHi, e);
                s = MathF.Pow(s, 1.6f); // keep existing contour for the drive scalar

                // Drive scalar for the shaper (SAT bumps ceiling)
                float kMaxBoost = Lerp(1.70f, 2.00f, satMix);
                float dynDrive = 1.0f + kMaxBoost * s;

                // --- Dry/Wet blend (SAT raises ceiling & arrives sooner) ---
                float wetFloor = Lerp(0.15f, 0.18f, satMix);
                float wetCeil = Lerp(0.30f, 0.60f, satMix);   // set 0.50f if you prefer
                float wetPow = Lerp(1.60f, 1.20f, satMix);
                float wet = wetFloor + (wetCeil - wetFloor) * MathF.Pow(s, wetPow);
                float dry = 1.0f - wet;

                // --- Asymmetry & bias vs s (SAT engages earlier & a bit stronger) ---
                float aGateLo = Lerp(0.88f, 0.75f, satMix);
                float aGateHi = Lerp(1.00f, 0.95f, satMix);
                float aPow = Lerp(1.30f, 1.15f, satMix);
                float asymMax = Lerp(0.15f, 0.18f, satMix);
                float kBiasBase = Lerp(0.028f, 0.032f, satMix);
                float kBiasHigh = Lerp(0.042f, 0.050f, satMix);

                float aGate = SmoothStep(aGateLo, aGateHi, s);
                aGate = MathF.Pow(aGate, aPow);
                float asym = asymMax * aGate;
                float kBiasEff = kBiasBase + (kBiasHigh - kBiasBase) * (asym / MathF.Max(1e-9f, asymMax));

                // 3) Pre-emphasis into the shaper
                float xPre = preLF.Process(x);

                // 4) Nonlinearity (ADAA)
                float ySh = Shaper.ProcessAsymTanhADAA(xPre + AntiDenorm, ref _adaaPrev[ch], dynDrive, asym, kBiasEff);

                // 4b) Remove tiny DC introduced by asymmetry and AntiDenorm
                ySh = dc.Process(ySh);

                // 5) Dry/Wet and post-EQ
                float y = dry * xPre + wet * ySh;

                // 6) Post filtering
                y = postLF.Process(y);
                y = postHS.Process(y);
                y = postHS2.Process(y);
                y = lpHfTamer.Process(y);

                // Sat makeup (pre-output trim), click-free because colorMix is ramped
                float satMakeupGain = Lerp(1.0f, DbToLin(3.0f), satMix);
                y *= satMakeupGain;

                // Output trim ramp
                outNow += outInc;
                output[i] = y * outNow;
            }

            lozBlend = MathF.Min(1f, MathF.Max(0f, lozBlend));
            lozPad = MathF.Min(1f, MathF.Max(0f, lozPad));

            _gainZ[ch] = g;
            _padZ[ch] = padNow;
            _phaseZ[ch] = phaseNow;
            _lozBlendZ[ch] = lozBlend;
            _lozPadZ[ch] = lozPad;
            _outputZ[ch] = outNow;
            _hpfMixZ[ch] = hpfMix;
            _satMixZ[ch] = satMix;
        }
    }

    // --- Helpers ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SmoothStep(float a, float b, float x)
    {
        float t = MathF.Min(1f, MathF.Max(0f, (x - a) / MathF.Max(1e-9f, b - a)));
        return t * t * (3f - 2f * t);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float DbToLin(float db) => (float)MathF.Pow(10.0f, db / 20.0f);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float ZapDenorm(float v)
    {
        // -400 dBFS threshold is safely inaudible, but avoids subnormals.
        return MathF.Abs(v) < 1e-20f ? 0f : v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private struct RmsDetector
    {
        private float _env;
        private float _aAtk, _aRel;

        public void Setup(double fs, double attackMs = 5.0, double releaseMs = 60.0)
        {
            _aAtk = (float)Math.Exp(-1.0 / (attackMs * 0.001 * fs));
            _aRel = (float)Math.Exp(-1.0 / (releaseMs * 0.001 * fs));
            _env = 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float x)
        {
            float e = x * x;
            float coeff = (e > _env) ? _aAtk : _aRel;
            _env = coeff * _env + (1f - coeff) * e;
            _env = ZapDenorm(_env);
            return MathF.Sqrt(_env) + 1e-12f;
        }
    }

    private struct Biquad
    {
        private float b0, b1, b2, a1, a2, z1, z2;

        public void SetLowShelf(double fs, double f0, double dBgain, double Q = 0.6)
        {
            double A = Math.Pow(10.0, dBgain / 40.0);
            double w0 = 2 * Math.PI * f0 / fs;
            double alpha = Math.Sin(w0) / (2 * Q);
            double cosw0 = Math.Cos(w0);

            double b0 = A * ((A + 1) - (A - 1) * cosw0 + 2 * Math.Sqrt(A) * alpha);
            double b1 = 2 * A * ((A - 1) - (A + 1) * cosw0);
            double b2 = A * ((A + 1) - (A - 1) * cosw0 - 2 * Math.Sqrt(A) * alpha);
            double a0 = (A + 1) + (A - 1) * cosw0 + 2 * Math.Sqrt(A) * alpha;
            double a1 = -2 * ((A - 1) + (A + 1) * cosw0);
            double a2 = (A + 1) + (A - 1) * cosw0 - 2 * Math.Sqrt(A) * alpha;

            this.b0 = (float)(b0 / a0);
            this.b1 = (float)(b1 / a0);
            this.b2 = (float)(b2 / a0);
            this.a1 = (float)(a1 / a0);
            this.a2 = (float)(a2 / a0);
            z1 = z2 = 0f;
        }

        public void SetHighShelf(double fs, double f0, double dBgain, double Q = 0.8)
        {
            double A = Math.Pow(10.0, dBgain / 40.0);
            double w0 = 2 * Math.PI * f0 / fs;
            double alpha = Math.Sin(w0) / (2 * Q);
            double cosw0 = Math.Cos(w0);

            double b0 = A * ((A + 1) + (A - 1) * cosw0 + 2 * Math.Sqrt(A) * alpha);
            double b1 = -2 * A * ((A - 1) + (A + 1) * cosw0);
            double b2 = A * ((A + 1) + (A - 1) * cosw0 - 2 * Math.Sqrt(A) * alpha);
            double a0 = (A + 1) - (A - 1) * cosw0 + 2 * Math.Sqrt(A) * alpha;
            double a1 = 2 * ((A - 1) - (A + 1) * cosw0);
            double a2 = (A + 1) - (A - 1) * cosw0 - 2 * Math.Sqrt(A) * alpha;

            this.b0 = (float)(b0 / a0);
            this.b1 = (float)(b1 / a0);
            this.b2 = (float)(b2 / a0);
            this.a1 = (float)(a1 / a0);
            this.a2 = (float)(a2 / a0);
            z1 = z2 = 0f;
        }

        public void SetHighPass(double fs, double f0, double Q = 0.707)
        {
            double w0 = 2 * Math.PI * f0 / fs;
            double cosw0 = Math.Cos(w0);
            double sinw0 = Math.Sin(w0);
            double alpha = sinw0 / (2 * Q);

            double b0 = (1 + cosw0) / 2;
            double b1 = -(1 + cosw0);
            double b2 = (1 + cosw0) / 2;
            double a0 = 1 + alpha;
            double a1 = -2 * cosw0;
            double a2 = 1 - alpha;

            this.b0 = (float)(b0 / a0);
            this.b1 = (float)(b1 / a0);
            this.b2 = (float)(b2 / a0);
            this.a1 = (float)(a1 / a0);
            this.a2 = (float)(a2 / a0);
            z1 = z2 = 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float x)
        {
            float y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
    }

    // One-pole low-pass (cheap) used as very gentle HF tamer
    private struct OnePoleLP
    {
        private float a, b, z;

        public void Setup(double fs, double cutoffHz)
        {
            double x = Math.Exp(-2.0 * Math.PI * cutoffHz / fs);
            b = (float)(1.0 - x);
            a = (float)x;
            z = 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float x)
        {
            // y[n] = b*x[n] + a*y[n-1]
            z = b * x + a * z;
            z = ZapDenorm(z);
            return z;
        }
    }

    // First-order high-pass DC blocker
    private struct DcBlock
    {
        private float a; // pole (0..1)
        private float x1, y1;

        public void Setup(double fs, double fHz = 10.0)
        {
            a = (float)Math.Exp(-2.0 * Math.PI * fHz / fs);
            x1 = y1 = 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float x)
        {
            float y = x - x1 + a * y1;
            x1 = ZapDenorm(x);
            y1 = ZapDenorm(y);
            return y;
        }
    }

    private struct OnePoleHP
    {
        private float a; // pole (0..1)
        private float x1, y1;

        public void Setup(double fs, double fHz)
        {
            a = (float)Math.Exp(-2.0 * Math.PI * fHz / fs);
            x1 = y1 = 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float x)
        {
            float y = x - x1 + a * y1;
            x1 = ZapDenorm(x);
            y1 = ZapDenorm(y);
            return y;
        }
    }

    private static class Shaper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ProcessAsymTanhADAA(float x, ref float xz, float dyn, float asym, float kBias)
        {
            // Asymmetric drive factors
            float dpos = dyn * (1f + asym);
            float dneg = dyn * (1f - asym);

            // Choose branch by midpoint (improves stability around zero-crossings)
            float xPrev = xz;
            float xMid = 0.5f * (x + xPrev);
            float d = (xMid >= 0f) ? dpos : dneg;

            // Slope normalization around zero using + side
            float dbPos = dpos * kBias;
            float sech2 = Sech2(dbPos);
            float norm = 1f / (dpos * sech2);

            // Antiderivative of tanh(d*(x+kBias)) − tanh(d*kBias):
            // F(x) = (1/d) * log(cosh(d*(x+kBias))) − x * tanh(d*kBias)
            float db = d * kBias;

            float dx = x - xPrev;
            float yAvg;
            if (MathF.Abs(dx) < 1e-9f)
            {
                // fallback to midpoint evaluation
                yAvg = MathF.Tanh(d * (xMid + kBias)) - MathF.Tanh(db);
            }
            else
            {
                float Fx = (1f / d) * LogCoshF(d * (x + kBias)) - x * MathF.Tanh(db);
                float Fx1 = (1f / d) * LogCoshF(d * (xPrev + kBias)) - xPrev * MathF.Tanh(db);
                yAvg = (Fx - Fx1) / dx;
            }

            xz = x;                 // update ADAA state
            float y = yAvg * norm;  // normalize local slope
            return 0.98f * MathF.Tanh(y); // very soft limiter
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float LogCoshF(float v) => (float)Math.Log(Math.Cosh(v));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Sech2(float x)
        {
            float c = (float)Math.Cosh(x);
            return 1f / (c * c);
        }
    }
}
