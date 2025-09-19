namespace SR.AnalogGain;
using NPlug;
public class AnalogGainModel : AudioProcessorModel
{
    public AnalogGainModel() : base("SR.AnalogGain")
    {
        AddByPassParameter();

        const double minDb = -60.0;
        const double maxDb = 12.0;
        double norm0dB = (0.0 - minDb) / (maxDb - minDb);

        Gain = AddParameter(new AudioParameter("Gain [-60 to +12 dB]", units: "dB", defaultNormalizedValue: norm0dB));
        // Explicitly set the normalized value to ensure 0 dB initialization
        Gain.NormalizedValue = norm0dB;

        // Output: -24..+12 dB (default 0 dB)
        const double outMin = -24.0, outMax = 12.0;
        double norm0Out = (0.0 - outMin) / (outMax - outMin);
        Output = AddParameter(new AudioParameter("Output [-24 to +12 dB]", units: "dB", defaultNormalizedValue: norm0Out));
        // Explicitly set the normalized value to ensure 0 dB initialization
        Output.NormalizedValue = norm0Out;

    }

    public AudioParameter Gain { get; }
    public AudioParameter Output { get; }
}
