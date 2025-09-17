namespace NPlug.SimpleGain;

public class SimpleGainModel : AudioProcessorModel
{
    public SimpleGainModel() : base("NPlug.SimpleGain")
    {
        AddByPassParameter();

        const double minDb = -60.0;
        const double maxDb = 12.0;
        double norm0dB = (0.0 - minDb) / (maxDb - minDb);

        Gain = AddParameter(new AudioParameter("Gain [-60 to +12 dB]", units: "dB", defaultNormalizedValue: norm0dB));

        // Output: -24..+12 dB (default 0 dB)
        const double outMin = -24.0, outMax = 12.0;
        double norm0Out = (0.0 - outMin) / (outMax - outMin);
        Output = AddParameter(new AudioParameter("Output [-24 to +12 dB]", units: "dB", defaultNormalizedValue: norm0Out));

    }

    public AudioParameter Gain { get; }
    public AudioParameter Output { get; }
}
