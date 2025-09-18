using NPlug;
namespace SR.AnalogGain;

public class AnalogGainController : AudioController<AnalogGainModel>
{
    public static readonly Guid ClassId = new("2418b185-051c-4d80-b17b-6ec45b953f76");

    // VST3 path: controller creates the "editor" view
    protected override IAudioPluginView? CreateView()
    {
        return new AnalogGainEditor(this, Model);
    }
}
