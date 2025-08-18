namespace NPlug.SimpleGain;

public class SimpleGainModel : AudioProcessorModel
{
    public SimpleGainModel() : base("NPlug.SimpleGain")
    {
        AddByPassParameter();

        const double minDb = -60.0;
        const double maxDb = 12.0;
        double norm0dB = (0.0 - minDb) / (maxDb - minDb); // 60 / 72 = 0.833333...

        Gain = AddParameter(new AudioParameter("Gain [-60 to +12 dB]", units: "dB", defaultNormalizedValue: norm0dB));
    }

    public AudioParameter Gain { get; }
}
