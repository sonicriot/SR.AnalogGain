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
    private OnePoleLP[] _lp18k = Array.Empty<OnePoleLP>(); // very gentle HF tamer
    private DcBlock[] _dc = Array.Empty<DcBlock>();

    private double _fs = 48000.0;
    private int _configuredChannels = -1;
    private double _configuredFs = -1.0;

    // Fixed mild coloration (pre/post shelves + subtle HF tilt)
    private const double PreLF_Freq = 140.0;   // Hz
    private const double PreLF_Gain = +2;      // dB
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
            _dc.Length != channels;

        // Allocate arrays if channel count changed
        if (sizeMismatch)
        {
            _rms = new RmsDetector[channels];
            _preLF = new Biquad[channels];
            _postLF = new Biquad[channels];
            _postHS = new Biquad[channels];
            _postHS2 = new Biquad[channels];
            _lp18k = new OnePoleLP[channels];
            _dc = new DcBlock[channels];
        }

        // (Re)configure filters if fs or channel count changed
        if (sizeMismatch || _configuredFs != _fs || _configuredChannels != channels)
        {
            for (int ch = 0; ch < channels; ch++)
            {
                _rms[ch].Setup(_fs, attackMs: 5.0, releaseMs: 60.0);
                _preLF[ch].SetLowShelf(_fs, PreLF_Freq, PreLF_Gain);
                _postLF[ch].SetLowShelf(_fs, PreLF_Freq, PostLF_Gain);
                _postHS[ch].SetHighShelf(_fs, PostHS_Freq, PostHS_Gain, 0.7);
                _postHS2[ch].SetHighShelf(_fs, 4000, 0.6, 0.5);
                _lp18k[ch].Setup(_fs, cutoffHz: 22000.0); // very gentle LP above audio band
                _dc[ch].Setup(_fs, 8.0); // DC blocker ~5–15 Hz
            }
            _configuredFs = _fs;
            _configuredChannels = channels;
        }
    }

    protected override void ProcessMain(in AudioProcessData data)
    {
        var inputBus = data.Input[0];
        var outputBus = data.Output[0];

        // Keep fs in sync with the host
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

        // Dynamic drive knee (RMS → s in 0..1)
        const float kneeLo = 0.200f; // ~ -14 dBFS
        const float kneeHi = 0.750f; // ~  -2.5 dBFS
        const float kMaxBoost = 1.7f; // max extra drive applied at s→1

        // Asymmetry / bias ranges (scaled by s)
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

            for (int i = 0; i < data.SampleCount; i++)
            {
                // 1) Linear gain
                float x = input[i] * gain;

                // 2) RMS level → dynamic drive
                float e = rms.Process(x);
                float s = SmoothStep(kneeLo, kneeHi, e); // 0..1
                s = MathF.Pow(s, 1.6f);

                // Dynamic drive amount (scales the shaper)
                float dynDrive = 1.0f + kMaxBoost * s;

                // Dry/Wet: late-onset, capped blend of the shaper
                // wetFloor=15% ensures non-zero harmonics at low s; wetCeil=30% max
                const float wetFloor = 0.15f; // 15%
                const float wetCeil = 0.30f; // 30%
                const float wetPow = 1.6f;  // late curve
                float wet = wetFloor + (wetCeil - wetFloor) * MathF.Pow(s, wetPow);
                float dry = 1.0f - wet;

                // Asymmetry & bias scale with s (more asym at the end of the knee)
                float aGate = SmoothStep(0.88f, 1.00f, s);
                aGate = MathF.Pow(aGate, 1.3f);
                float asym = asymMax * aGate;
                float kBiasEff = kBiasBase + (kBiasHigh - kBiasBase) * (asym / asymMax);

                // 3) Pre-emphasis into the shaper
                float xPre = preLF.Process(x);

                // 4) Nonlinearity (choose ONE):
                //    a) asymmetrical (current default)
                float ySh = Shaper.ProcessAsymTanh(xPre, dynDrive, asym, kBiasEff);

                //    b) symmetrical (uncomment to try a cleaner symmetric tanh)
                // float ySh = Shaper.ProcessNormalizedTanh(xPre, dynDrive);

                // 4b) Remove any tiny DC introduced by asymmetry
                ySh = dc.Process(ySh);

                // Dry/Wet mix
                float y = dry * xPre + wet * ySh;

                // 5) Post-EQ and HF tame
                y = postLF.Process(y);
                y = postHS.Process(y);
                y = postHS2.Process(y);
                y = lp18k.Process(y);

                output[i] = y;
            }
        }
    }

    // ---------- Helpers ----------

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

    // One-pole low-pass (cheap) used as gentle HF tamer
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
        private float a;    // pole (0..1)
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
        // Subtle static bias to seed a small 2nd harmonic
        private const float kBias = 0.006f;

        // Symmetric normalized tanh (kept for easy A/B; call is commented in ProcessMain)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ProcessNormalizedTanh(float x, float dyn)
        {
            // Normalize slope around 0 so drive doesn't harden the mid region
            // d/dx tanh(d*(x+b)) at x=0 is d*sech^2(d*b)
            float d = dyn;
            float db = d * kBias;
            float sech2 = Sech2(db);
            float norm = 1.0f / (d * sech2);

            float y = MathF.Tanh(d * (x + kBias)) - MathF.Tanh(db);
            y *= norm; // ~unit slope near 0

            // soft limiter to avoid excessive peaks
            return 0.98f * MathF.Tanh(y);
        }

        // Asymmetric normalized tanh with bias (current default)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ProcessAsymTanh(float x, float dyn, float asym, float kBias)
        {
            float dpos = dyn * (1f + asym);
            float dneg = dyn * (1f - asym);

            float db = dpos * kBias;                 // normalize using + side
            float sech2 = Sech2(db);
            float norm = 1f / (dpos * sech2);

            float y = x >= 0
                ? MathF.Tanh(dpos * (x + kBias)) - MathF.Tanh(db)
                : MathF.Tanh(dneg * (x + kBias)) - MathF.Tanh(db);

            y *= norm;
            return 0.98f * MathF.Tanh(y);            // soft limiter
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Sech2(float x)
        {
            // sech(x) = 1 / cosh(x);  sech^2(x) = 1 / cosh^2(x)
            float c = MathF.Cosh(x);
            return 1.0f / (c * c);
        }
    }
}
