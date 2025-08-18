
namespace NPlug.SimpleGain;

public class SimpleGainController : AudioController<SimpleGainModel>
{
    public static readonly Guid ClassId = new("4f38b617-4726-4941-b18d-e2d776ec8635");

    // VST3 path: controller creates the "editor" view
    protected override IAudioPluginView? CreateView()
    {
        return new SimpleGainEditor(this, Model);
    }
}
