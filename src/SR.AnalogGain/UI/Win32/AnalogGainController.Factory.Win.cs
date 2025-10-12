#if WINDOWS
using SR.AnalogGain.UI;
using SR.AnalogGain.UI.Win32;

namespace SR.AnalogGain
{
    public sealed partial class AnalogGainController
    {
        private static partial IEditorFactory CreateEditorFactory() => new EditorFactoryWin();
    }

    internal sealed class EditorFactoryWin : IEditorFactory
    {
        public IAnalogGainEditorView CreateEditor(AnalogGainController controller, AnalogGainModel model)
            => new AnalogGainEditorWin(controller, model);
    }
}
#endif
