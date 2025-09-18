using System;
using System.Runtime.CompilerServices;
using NPlug;

namespace NPlug.SimpleGain;

public class SimpleGainProcessor : AudioProcessor<SimpleGainModel>
{
    public static readonly Guid ClassId = new("6cc2ddeb-b6ea-4778-af48-45b2a8df59e2");

    public SimpleGainProcessor() : base(AudioSampleSizeSupport.Float32) { }

    public override Guid ControllerClassId => SimpleGainController.ClassId;

    // --- Per-channel state ---
    private RmsDetector[] _rms = Array.Empty<RmsDetector>();
    private Biquad[] _preLF = Array.Empty<Biquad>();
    private Biquad[] _postLF = Array.Empty<Biquad>();
    private Biquad[] _postHS = Array.Empty<Biquad>();
    private Biquad[] _postHS2 = Array.Empty<Biquad>();
    private OnePoleLP[] _lp18k = Array.Empty<OnePoleLP>();   // very gentle HF tamer
    private DcBlock[] _dc = Array.Empty<DcBlock>();
    private float[] _gainZ = Array.Empty<float>();           // smoothed gain
    private float[] _adaaPrev = Array.Empty<float>();        // ADAA state
    private float[] _outputZ = Array.Empty<float>();

    private double _fs = 48000.0;
    private int _configuredChannels = -1;
    private double _configuredFs = -1.0;

    // Fixed mild coloration (pre/post shelves + subtle HF tilt)
    private const double PreLF_Freq = 140.0;   // Hz
    private const double PreLF_Gain = +2.0;    // dB
    private const double PostLF_Gain = -1.7;   // dB (compensates PreLF)
    private const double PostHS_Freq = 18000.0; // Hz
    private const double PostHS_Gain = -1.4;   // dB (subtle tilt)

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
            _lp18k.Length != channels ||
            _dc.Length != channels ||
            _gainZ.Length != channels ||
            _adaaPrev.Length != channels ||
            _outputZ.Length != channels;

        if (sizeMismatch)
        {
            _rms = new RmsDetector[channels];
            _preLF = new Biquad[channels];
            _postLF = new Biquad[channels];
            _postHS = new Biquad[channels];
            _postHS2 = new Biquad[channels];
            _lp18k = new OnePoleLP[channels];
            _dc = new DcBlock[channels];
            _gainZ = new float[channels];
            _adaaPrev = new float[channels];
            _outputZ = new float[channels];
        }

        if (sizeMismatch || _configuredFs != _fs || _configuredChannels != channels)
        {
            for (int ch = 0; ch < channels; ch++)
            {
                _rms[ch].Setup(_fs, attackMs: 5.0, releaseMs: 60.0);
                _preLF[ch].SetLowShelf(_fs, PreLF_Freq, PreLF_Gain);
                _postLF[ch].SetLowShelf(_fs, PreLF_Freq, PostLF_Gain);
                _postHS[ch].SetHighShelf(_fs, PostHS_Freq, PostHS_Gain, 0.7);
                _postHS2[ch].SetHighShelf(_fs, 4000.0, 0.6, 0.5);
                _lp18k[ch].Setup(_fs, cutoffHz: 22000.0);
                _dc[ch].Setup(_fs, 8.0);
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
        float gain = (float)Math.Pow(10.0, gainDb / 20.0);

        // Output mapping: normalized [0..1] → dB → linear
        const double outMinDb = -24.0;
        const double outMaxDb = +12.0;
        double outNorm = Model.Output.NormalizedValue;
        double outDb = outMinDb + outNorm * (outMaxDb - outMinDb);

        float outTarget = (float)Math.Pow(10.0, outDb / 20.0);

        // RMS → drive curve
        const float kneeLo = 0.200f;  // ~ -14 dBFS
        const float kneeHi = 0.750f;  // ~  -2.5 dBFS
        const float kMaxBoost = 1.7f; // max extra drive at s→1

        // Asymmetry / bias ranges
        const float asymMax = 0.15f;
        const float kBiasBase = 0.028f; // effective bias at low drive
        const float kBiasHigh = 0.042f; // effective bias at high drive

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
            ref var lp18k = ref _lp18k[ch];
            ref var dc = ref _dc[ch];

            // initialize smoothed gain on first buffer
            if (_gainZ[ch] == 0f) _gainZ[ch] = gain;

            float g = _gainZ[ch];
            float gInc = (gain - g) / Math.Max(1, data.SampleCount);

            if (_outputZ[ch] == 0f) _outputZ[ch] = outTarget;

            float outNow = _outputZ[ch];
            float outInc = (outTarget - outNow) / Math.Max(1, data.SampleCount);

            for (int i = 0; i < data.SampleCount; i++)
            {
                // 1) Linear gain (smoothed)
                g += gInc;
                float x = input[i] * g;

                // 2) RMS envelope → drive control (s in 0..1)
                float e = rms.Process(x);
                float s = SmoothStep(kneeLo, kneeHi, e);
                s = MathF.Pow(s, 1.6f);

                // Drive scalar for the shaper
                float dynDrive = 1.0f + kMaxBoost * s;

                // Dry/Wet blend (late onset)
                const float wetFloor = 0.15f;
                const float wetCeil = 0.30f;
                const float wetPow = 1.6f;
                float wet = wetFloor + (wetCeil - wetFloor) * MathF.Pow(s, wetPow);
                float dry = 1.0f - wet;

                // Asymmetry & bias vs s
                float aGate = SmoothStep(0.88f, 1.00f, s);
                aGate = MathF.Pow(aGate, 1.3f);
                float asym = asymMax * aGate;
                float kBiasEff = kBiasBase + (kBiasHigh - kBiasBase) * (asym / asymMax);

                // 3) Pre-emphasis into the shaper
                float xPre = preLF.Process(x);

                // 4) Nonlinearity (ADAA).
                float ySh = Shaper.ProcessAsymTanhADAA(xPre, ref _adaaPrev[ch], dynDrive, asym, kBiasEff);

                // 4b) Remove tiny DC introduced by asymmetry
                ySh = dc.Process(ySh);

                // 5) Dry/Wet and post-EQ
                float y = dry * xPre + wet * ySh;

                // 6) Post filtering
                y = postLF.Process(y);
                y = postHS.Process(y);
                y = postHS2.Process(y);
                y = lp18k.Process(y);

                outNow += outInc;    // final output trim
                output[i] = y * outNow;
                
            }
            _gainZ[ch] = g;
            _outputZ[ch] = outNow;
        }
    }

    // --- Helpers ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SmoothStep(float a, float b, float x)
    {
        float t = MathF.Min(1f, MathF.Max(0f, (x - a) / (b - a)));
        return t * t * (3f - 2f * t);
    }

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
            return MathF.Sqrt(_env) + 1e-12f;
        }
    }

    private struct Biquad
    {
        private float a0, a1, a2, b1, b2, z1, z2;

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

            this.a0 = (float)(b0 / a0);
            this.a1 = (float)(b1 / a0);
            this.a2 = (float)(b2 / a0);
            this.b1 = (float)(a1 / a0);
            this.b2 = (float)(a2 / a0);
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

            this.a0 = (float)(b0 / a0);
            this.a1 = (float)(b1 / a0);
            this.a2 = (float)(b2 / a0);
            this.b1 = (float)(a1 / a0);
            this.b2 = (float)(a2 / a0);
            z1 = z2 = 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float x)
        {
            float y = a0 * x + z1;
            z1 = a1 * x - b1 * y + z2;
            z2 = a2 * x - b2 * y;
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
            x1 = x;
            y1 = y;
            return y;
        }
    }

    private static class Shaper
    {
        // Small static bias to seed a tiny 2nd harmonic (used in normalization)
        private const float kBias = 0.006f;

        // ADAA version of the asymmetric normalized tanh
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
