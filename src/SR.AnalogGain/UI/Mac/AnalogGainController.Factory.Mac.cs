#if MACOS
using SR.AnalogGain.UI;
using SR.AnalogGain.UI.Mac;

namespace SR.AnalogGain
{
    public sealed partial class AnalogGainController
    {
        private static partial IEditorFactory CreateEditorFactory() => new EditorFactoryMac();
    }

    internal sealed class EditorFactoryMac : IEditorFactory
    {
        public IAnalogGainEditorView CreateEditor(AnalogGainController controller, AnalogGainModel model)
            => new AnalogGainEditorMac(controller, model);
    }
}
#endif
