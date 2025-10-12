using NPlug;

namespace SR.AnalogGain.UI
{
    public interface IEditorFactory
    {
        IAnalogGainEditorView CreateEditor(AnalogGainController controller, AnalogGainModel model);
    }
}
