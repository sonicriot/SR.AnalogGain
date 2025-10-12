using System;
using NPlug;
using NPlug.IO;
using SR.AnalogGain.UI;

namespace SR.AnalogGain
{
    public sealed partial class AnalogGainController : AudioController<AnalogGainModel>
    {
        public static readonly Guid ClassId = new("2418b185-051c-4d80-b17b-6ec45b953f76");

        private IAnalogGainEditorView? _editor;
        private readonly IEditorFactory _editorFactory;

        public AnalogGainController()
        {
            _editorFactory = CreateEditorFactory(); // platform-specific via partial
        }

        // VST3 path: controller creates the "editor" view
        protected override IAudioPluginView? CreateView()
        {
            _editor = _editorFactory.CreateEditor(this, Model);
            return _editor;
        }

        // Called when parameters are changed (UI or host)
        protected override void OnParameterValueChanged(AudioParameter parameter, bool parameterValueChangedFromHost)
        {
            base.OnParameterValueChanged(parameter, parameterValueChangedFromHost);
            _editor?.RefreshParameter(parameter);
        }

        // Called when the host restores the component state (preset loading)
        protected override void RestoreComponentState(PortableBinaryReader reader)
        {
            base.RestoreComponentState(reader);
            _editor?.RefreshUI();
        }

        // Called when the host restores the controller state
        protected override void RestoreState(PortableBinaryReader reader)
        {
            base.RestoreState(reader);
            _editor?.RefreshUI();
        }

        // Clean up editor reference when view is removed
        public void OnEditorClosed() => _editor = null;

        // IMPORTANT: non-void partial must be explicit about accessibility
        private static partial IEditorFactory CreateEditorFactory();
    }
}
