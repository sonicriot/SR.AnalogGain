using NPlug;

namespace SR.AnalogGain.UI
{
    /// <summary>
    /// Adds editor-sync hooks your controller expects, on top of IAudioPluginView.
    /// Both Windows and macOS editors should implement this.
    /// </summary>
    public interface IAnalogGainEditorView : IAudioPluginView
    {
        void RefreshUI();
        void RefreshParameter(AudioParameter parameter);
    }
}
