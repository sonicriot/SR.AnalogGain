using System;
using System.Runtime.CompilerServices;
using NPlug;

namespace NPlug.SimpleGain;

public class SimpleGainProcessor : AudioProcessor<SimpleGainModel>
{
    public static readonly Guid ClassId = new("6cc2ddeb-b6ea-4778-af48-45b2a8df59e2");

    public SimpleGainProcessor() : base(AudioSampleSizeSupport.Float32) { }

    public override Guid ControllerClassId => SimpleGainController.ClassId;

    // --- Estados por canal ---
    private RmsDetector[] _rms = Array.Empty<RmsDetector>();
    private Biquad[] _preLF = Array.Empty<Biquad>();
    private Biquad[] _postLF = Array.Empty<Biquad>();
    private Biquad[] _postHS = Array.Empty<Biquad>();
    private Biquad[] _postHS2 = Array.Empty<Biquad>();
    private OnePoleLP[] _lp18k = Array.Empty<OnePoleLP>(); // HF tamer

    private double _fs = 48000.0;
    private int _configuredChannels = -1;
    private double _configuredFs = -1.0;

    // Color fijo (ligero)
    private const double PreLF_Freq = 140.0;    // Hz
    private const double PreLF_Gain = +2;     // dB  (antes +3 dB)
    private const double PostLF_Gain = -1.7;    // dB  (compensa)
    private const double PostHS_Freq = 18000.0; // Hz
    private const double PostHS_Gain = -1.4;    // dB  (tilt sutil)

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
            _lp18k.Length != channels;

        if (sizeMismatch)
        {
            _rms = new RmsDetector[channels];
            _preLF = new Biquad[channels];
            _postLF = new Biquad[channels];
            _postHS = new Biquad[channels];
            _postHS2 = new Biquad[channels];
            _lp18k = new OnePoleLP[channels];
        }

        if (sizeMismatch || _configuredFs != _fs || _configuredChannels != channels)
        {
            for (int ch = 0; ch < channels; ch++)
            {
                _rms[ch].Setup(_fs, attackMs: 5.0, releaseMs: 60.0);
                _preLF[ch].SetLowShelf(_fs, PreLF_Freq, PreLF_Gain);
                _postLF[ch].SetLowShelf(_fs, PreLF_Freq, PostLF_Gain);
                _postHS[ch].SetHighShelf(_fs, PostHS_Freq, PostHS_Gain, 0.7);
                _postHS2[ch].SetHighShelf(_fs, 4000, 0.6, 0.5);
                _lp18k[ch].Setup(_fs, cutoffHz: 22000.0); // HF tamer
            }
            _configuredFs = _fs;
            _configuredChannels = channels;
        }
    }

    protected override void ProcessMain(in AudioProcessData data)
    {
        var inputBus = data.Input[0];
        var outputBus = data.Output[0];

        double sampleRate = ProcessSetupData.SampleRate;
        if (sampleRate > 1000.0 && sampleRate != _fs)
        {
            _fs = sampleRate;
            _configuredFs = -1.0;
        }

        EnsureChannelState(inputBus.ChannelCount);

        // Mapeo Gain (igual que tu versión original)
        const double minDb = -60.0;
        const double maxDb = +12.0;
        double normalized = Model.Gain.NormalizedValue;
        double gainDb = minDb + normalized * (maxDb - minDb);
        float gain = (float)Math.Pow(10.0, gainDb / 20.0);

        // Knee del drive dinámico
        const float kneeLo = 0.063f; // ~ -24 dBFS
        const float kneeHi = 0.501f; // ~ -6 dBFS
        const float kMaxBoost = 2.2f;

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

            for (int i = 0; i < data.SampleCount; i++)
            {
                // 1) Gain lineal
                float x = input[i] * gain;

                // 2) Nivel RMS post-gain → drive dinámico
                float e = rms.Process(x);
                float s = SmoothStep(kneeLo, kneeHi, e); // 0..1
                float dynDrive = 1.0f + kMaxBoost * s;

                // 3) Pre énfasis hacia el shaper
                float xPre = preLF.Process(x);

                // 4) No linealidad suavizada (tanh normalizada + asimetría ligera)
                float y = Shaper.ProcessNormalizedTanh(xPre, dynDrive);

                // 5) Compensaciones / HF tame
                y = postLF.Process(y);
                y = postHS.Process(y);
                y = postHS2.Process(y);
                y = lp18k.Process(y); // LP 18 kHz muy suave

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

    // 1er orden LP (muy barato) para suavizar aspereza alta
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
            // y = b*x + a*y[z-1]
            z = b * x + a * z;
            return z;
        }
    }

    private static class Shaper
    {
        // Asimetría sutil (2º armónico muy ligero)
        private const float kBias = 0.012f;

        // Curva más dulce: tanh normalizada + y^3 MUY pequeño
        private const float kA = 0.012f; // antes 0.028 → menos 3º armónico

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ProcessNormalizedTanh(float x, float dyn)
        {
            // Normalizamos la pendiente en cero para evitar “endurecer” con drive alto.
            // Derivada en 0 de tanh(d*(x+b)) es d*sech^2(d*b).
            float d = dyn;
            float db = d * kBias;
            float sech2 = Sech2(db);           // sech^2(d*bias)
            float norm = 1.0f / (d * sech2);   // normalizador de pendiente

            float y = MathF.Tanh(d * (x + kBias)) - MathF.Tanh(db);
            y *= norm; // pendiente ~1 cerca de 0

            // toque sutil de 3er armónico
            float y3 = y * y * y;
            y = y + kA * y3;

            // limit muy suave
            return 0.98f * MathF.Tanh(y);
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
