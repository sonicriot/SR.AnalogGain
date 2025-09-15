using System;
using System.Runtime.CompilerServices;
using NPlug;

namespace NPlug.SimpleGain
{
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
        private OnePoleLP[] _lp18k = Array.Empty<OnePoleLP>(); // HF tamer

        // --- Runtime ---
        private double _fs = 48_000.0;

        // --- Fixed tone shaping (subtle) ---
        private const double PreLF_Freq = 140.0;     // Hz
        private const double PreLF_Gain = +2.0;      // dB
        private const double PostLF_Gain = -2.0;     // dB (compensation)
        private const double PostHS_Freq = 14_000.0; // Hz
        private const double PostHS_Gain = -0.6;     // dB (very light tilt)

        // --- UI gain mapping ---
        private const double GainMinDb = -60.0;
        private const double GainMaxDb = +12.0;

        protected override bool Initialize(AudioHostApplication host)
        {
            // Keep I/O identical
            AddAudioInput("AudioInput", SpeakerArrangement.SpeakerStereo);
            AddAudioOutput("AudioOutput", SpeakerArrangement.SpeakerStereo);
            return true;
        }

        protected override void OnActivate(bool isActive)
        {
            if (!isActive) return;

            // Host provides setup here in NPlug 0.4.x
            _fs = ProcessSetupData.SampleRate;
            EnsureChannelState(null);
        }

        /// <summary>
        /// Ensures per-channel processors are allocated and configured for the given channel count.
        /// </summary>
        private void EnsureChannelState(int? dataChannelCount)
        {
            int channels = dataChannelCount ?? 2; // default to stereo if unknown
            if (_rms.Length == channels &&
                _preLF.Length == channels &&
                _postLF.Length == channels &&
                _postHS.Length == channels &&
                _lp18k.Length == channels)
            {
                return;
            }

            _rms = new RmsDetector[channels];
            _preLF = new Biquad[channels];
            _postLF = new Biquad[channels];
            _postHS = new Biquad[channels];
            _lp18k = new OnePoleLP[channels];

            for (int ch = 0; ch < channels; ch++)
            {
                _rms[ch].Setup(_fs, attackMs: 5.0, releaseMs: 60.0);
                _preLF[ch].SetLowShelf(_fs, PreLF_Freq, PreLF_Gain);
                _postLF[ch].SetLowShelf(_fs, PreLF_Freq, PostLF_Gain);
                _postHS[ch].SetHighShelf(_fs, PostHS_Freq, PostHS_Gain);
                _lp18k[ch].Setup(_fs, cutoffHz: 18_000.0); // very gentle HF smoother
            }
        }

        protected override void ProcessMain(in AudioProcessData data)
        {
            var inputBus = data.Input[0];
            var outputBus = data.Output[0];

            // Keep channel state in sync with the current audio bus
            EnsureChannelState(inputBus.ChannelCount);

            // Map normalized UI gain [0..1] to dB and then to linear
            double normalized = Model.Gain.NormalizedValue;
            double gainDb = GainMinDb + normalized * (GainMaxDb - GainMinDb);
            float gain = (float)Math.Pow(10.0, gainDb / 20.0);

            // Dynamic drive knee (kept identical)
            const float kneeLo = 0.063f; // ~ -24 dBFS
            const float kneeHi = 0.501f; // ~ -6 dBFS
            const float kMaxBoost = 2.2f;

            int channels = inputBus.ChannelCount;
            int n = data.SampleCount;

            for (int ch = 0; ch < channels; ch++)
            {
                // Fast spans for current channel
                var input = inputBus.GetChannelSpanAsFloat32(ProcessSetupData, data, ch);
                var output = outputBus.GetChannelSpanAsFloat32(ProcessSetupData, data, ch);

                ref var rms = ref _rms[ch];
                ref var preLF = ref _preLF[ch];
                ref var postLF = ref _postLF[ch];
                ref var postHS = ref _postHS[ch];
                ref var lp18k = ref _lp18k[ch];

                for (int i = 0; i < n; i++)
                {
                    // 1) Linear gain
                    float x = input[i] * gain;

                    // 2) Post-gain RMS → dynamic drive amount
                    float e = rms.Process(x);
                    float s = SmoothStep(kneeLo, kneeHi, e);  // 0..1
                    float dynDrive = 1.0f + kMaxBoost * s;

                    // 3) Pre-emphasis into the shaper
                    float xPre = preLF.Process(x);

                    // 4) Soft non-linearity (normalized tanh + subtle asymmetry)
                    float y = Shaper.ProcessNormalizedTanh(xPre, dynDrive);

                    // 5) Compensations / HF taming
                    y = postLF.Process(y);
                    y = postHS.Process(y);
                    y = lp18k.Process(y);

                    output[i] = y;
                }
            }
        }

        // ---------- Helpers ----------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float SmoothStep(float a, float b, float x)
        {
            // SmoothStep(a,b,x) in [0..1] with C1 continuity
            float t = MathF.Min(1f, MathF.Max(0f, (x - a) / (b - a)));
            return t * t * (3f - 2f * t);
        }

        private struct RmsDetector
        {
            private float _env;
            private float _aAtk, _aRel;

            public void Setup(double fs, double attackMs = 5.0, double releaseMs = 60.0)
            {
                // Exponential envelope coefficients
                _aAtk = (float)Math.Exp(-1.0 / (attackMs * 0.001 * fs));
                _aRel = (float)Math.Exp(-1.0 / (releaseMs * 0.001 * fs));
                _env = 0f;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float Process(float x)
            {
                // RMS detector with attack/release
                float e = x * x;
                float coeff = (e > _env) ? _aAtk : _aRel;
                _env = coeff * _env + (1f - coeff) * e;
                return MathF.Sqrt(_env) + 1e-12f; // small bias avoids denormals/div0
            }
        }

        private struct Biquad
        {
            // Direct Form I with two delay registers
            private float a0, a1, a2, b1, b2, z1, z2;

            public void SetLowShelf(double fs, double f0, double dBgain, double Q = 1.41421356237)
            {
                double A = Math.Pow(10.0, dBgain / 40.0);
                double w0 = 2 * Math.PI * f0 / fs;
                double alpha = Math.Sin(w0) / (2 * Q);
                double cosw0 = Math.Cos(w0);

                double b0 = A * ((A + 1) - (A - 1) * cosw0 + 2 * Math.Sqrt(A) * alpha);
                double b1 = 2 * A * ((A - 1) - (A + 1) * cosw0);
                double b2 = A * ((A + 1) - (A - 1) * cosw0 - 2 * Math.Sqrt(A) * alpha);
                double a0d = (A + 1) + (A - 1) * cosw0 + 2 * Math.Sqrt(A) * alpha;
                double a1d = -2 * ((A - 1) + (A + 1) * cosw0);
                double a2d = (A + 1) + (A - 1) * cosw0 - 2 * Math.Sqrt(A) * alpha;

                a0 = (float)(b0 / a0d);
                a1 = (float)(b1 / a0d);
                a2 = (float)(b2 / a0d);
                b1 = (float)(a1d / a0d);
                b2 = (float)(a2d / a0d);
                z1 = z2 = 0f;
            }

            public void SetHighShelf(double fs, double f0, double dBgain, double Q = 1.41421356237)
            {
                double A = Math.Pow(10.0, dBgain / 40.0);
                double w0 = 2 * Math.PI * f0 / fs;
                double alpha = Math.Sin(w0) / (2 * Q);
                double cosw0 = Math.Cos(w0);

                double b0 = A * ((A + 1) + (A - 1) * cosw0 + 2 * Math.Sqrt(A) * alpha);
                double b1 = -2 * A * ((A - 1) + (A + 1) * cosw0);
                double b2 = A * ((A + 1) + (A - 1) * cosw0 - 2 * Math.Sqrt(A) * alpha);
                double a0d = (A + 1) - (A - 1) * cosw0 + 2 * Math.Sqrt(A) * alpha;
                double a1d = 2 * ((A - 1) - (A + 1) * cosw0);
                double a2d = (A + 1) - (A - 1) * cosw0 - 2 * Math.Sqrt(A) * alpha;

                a0 = (float)(b0 / a0d);
                a1 = (float)(b1 / a0d);
                a2 = (float)(b2 / a0d);
                b1 = (float)(a1d / a0d);
                b2 = (float)(a2d / a0d);
                z1 = z2 = 0f;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float Process(float x)
            {
                // Transposed Direct Form II (1 multiply saved on feedback path)
                float y = a0 * x + z1;
                z1 = a1 * x - b1 * y + z2;
                z2 = a2 * x - b2 * y;
                return y;
            }
        }

        // Cheap 1st-order LP to shave very high frequencies gently
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

        private static class Shaper
        {
            // Very subtle asymmetry to introduce a light 2nd harmonic
            private const float kBias = 0.012f;

            // Slight 3rd-harmonic contribution after slope-normalized tanh
            private const float kA = 0.012f;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float ProcessNormalizedTanh(float x, float dyn)
            {
                // Keep unit slope around zero regardless of drive (prevents "hardening").
                // d/dx[tanh(d*(x+b))] at x=0 equals d*sech^2(d*b).
                float d = dyn;
                float db = d * kBias;
                float sech2 = Sech2(db);
                float norm = 1.0f / (d * sech2);

                float y = MathF.Tanh(d * (x + kBias)) - MathF.Tanh(db);
                y *= norm; // ~unit slope near zero

                // Subtle 3rd harmonic
                float y3 = y * y * y;
                y = y + kA * y3;

                // Very soft final limiting
                return 0.98f * MathF.Tanh(y);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static float Sech2(float x)
            {
                // sech^2(x) = 1 / cosh^2(x)
                float c = MathF.Cosh(x);
                return 1.0f / (c * c);
            }
        }
    }
}
