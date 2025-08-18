
namespace NPlug.SimpleGain;

public class SimpleGainProcessor : AudioProcessor<SimpleGainModel>
{
    public static readonly Guid ClassId = new("6cc2ddeb-b6ea-4778-af48-45b2a8df59e2");

    public SimpleGainProcessor() : base(AudioSampleSizeSupport.Float32)
    {
    }

    public override Guid ControllerClassId => SimpleGainController.ClassId;

    protected override bool Initialize(AudioHostApplication host)
    {
        AddAudioInput("AudioInput", SpeakerArrangement.SpeakerStereo);
        AddAudioOutput("AudioOutput", SpeakerArrangement.SpeakerStereo);
        return true;
    }

    protected override void OnActivate(bool isActive)
    {
        // No state to initialize for a basic gain processor
    }

    protected override void ProcessMain(in AudioProcessData data)
    {
        var inputBus = data.Input[0];
        var outputBus = data.Output[0];

        // Convert normalized gain value to dB (range: -60 dB to +12 dB)
        const double minDb = -60.0;
        const double maxDb = 12.0;
        double normalized = Model.Gain.NormalizedValue;
        double gainDb = minDb + normalized * (maxDb - minDb);
        float gain = (float)Math.Pow(10.0, gainDb / 20.0); // convert dB to linear

        for (int channel = 0; channel < inputBus.ChannelCount; channel++)
        {
            var input = inputBus.GetChannelSpanAsFloat32(ProcessSetupData, data, channel);
            var output = outputBus.GetChannelSpanAsFloat32(ProcessSetupData, data, channel);

            for (int i = 0; i < data.SampleCount; i++)
            {
                output[i] = input[i] * gain;
            }
        }
    }
}
