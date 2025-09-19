using System.Threading.Tasks;
using NPlug;
using NPlug.IO;
namespace SR.AnalogGain;

public class AnalogGainController : AudioController<AnalogGainModel>
{
    public static readonly Guid ClassId = new("2418b185-051c-4d80-b17b-6ec45b953f76");

    private AnalogGainEditor? _editor;

    // VST3 path: controller creates the "editor" view
    protected override IAudioPluginView? CreateView()
    {
        _editor = new AnalogGainEditor(this, Model);
        return _editor;
    }

    // Called when parameters are changed (both from UI and externally like host preset loading)
    protected override void OnParameterValueChanged(AudioParameter parameter, bool parameterValueChangedFromHost)
    {
        // Call base implementation first to handle the parameter change
        base.OnParameterValueChanged(parameter, parameterValueChangedFromHost);

        //Refresh UI when parameters change externally
        if (_editor != null && parameterValueChangedFromHost)
        {
            // Force UI refresh for any parameter change (for debugging)
            _editor.RefreshUI();
        }
    }

    // Called when the host restores the component state (preset loading)
    protected override void RestoreComponentState(PortableBinaryReader reader)
    {
        base.RestoreComponentState(reader);
        
        // Force UI refresh after state restoration
        _editor?.RefreshUI();
    }

    // Called when the host restores the controller state
    protected override void RestoreState(PortableBinaryReader reader)
    {
        base.RestoreState(reader);
        
        // Force UI refresh after state restoration
        _editor?.RefreshUI();
    }

    // Clean up editor reference when view is removed
    public void OnEditorClosed()
    {
        _editor = null;
    }
}
