using System;
using NPlug;

namespace NPlug.SimpleGain;

public sealed class SimpleGainEditor : IAudioPluginView
{
    private readonly SimpleGainController _controller;
    private readonly SimpleGainModel _model;
    private readonly UI.Win32.FixedGainWindow _window;

    private IAudioPluginFrame? _frame;
    private float _scale = 1.0f; // DPI/content scale

    // Base logical size (before scaling)
    private const int BaseWidth = 320;
    private const int BaseHeight = 140;

    public SimpleGainEditor(SimpleGainController controller, SimpleGainModel model)
    {
        _controller = controller;
        _model = model;

        _window = new UI.Win32.FixedGainWindow(
            width: BaseWidth,
            height: BaseHeight,
            minDb: -60.0,
            maxDb: 12.0,
            getNormalized: () => (float)_model.Gain.NormalizedValue,
            //setNormalized: v =>
            //{
            //    var nv = (float)Math.Clamp(v, 0.0, 1.0);
            //    var p = _model.Gain;
            //    _controller.BeginEditParameter(p);
            //    p.NormalizedValue = nv;              // triggers OnParameterValueChanged (UI-originated)
            //    _controller.EndEditParameter();
            //}
            beginEdit: () => _controller.BeginEditParameter(_model.Gain),
            performEdit: v => _model.Gain.NormalizedValue = (float)Math.Clamp(v, 0, 1),
            endEdit: () => _controller.EndEditParameter()
        );
    }

    // --- IAudioPluginView (new API) ---

    public bool IsPlatformTypeSupported(AudioPluginViewPlatform platform)
        => platform == AudioPluginViewPlatform.Hwnd;

    public void Attached(nint parent, AudioPluginViewPlatform type)
    {
        if (type != AudioPluginViewPlatform.Hwnd) return;

        _window.AttachToParent(parent);

        int w = (int)Math.Round(BaseWidth * _scale);
        int h = (int)Math.Round(BaseHeight * _scale);
        _window.SetBounds(0, 0, w, h);
        _window.RefreshLabel();
    }

    public void Removed() => _window.Destroy();

    public void OnWheel(float distance)
    {
        // wheel: +/- 0.5 dB per notch
        AdjustDb(Math.Sign(distance) * 0.5);
    }

    public void OnKeyDown(ushort key, short keyCode, short modifiers)
    {
        const int VK_LEFT = 0x25, VK_RIGHT = 0x27;
        if (keyCode == VK_LEFT) AdjustDb(-0.5);
        else if (keyCode == VK_RIGHT) AdjustDb(+0.5);
    }

    public void OnKeyUp(ushort key, short keyCode, short modifiers)
    {
        // no-op
    }

    public ViewRectangle Size
    {
        get
        {
            int w = (int)Math.Round(BaseWidth * _scale);
            int h = (int)Math.Round(BaseHeight * _scale);
            return new ViewRectangle(0, 0, w, h);
        }
    }

    public void OnSize(ViewRectangle newSize)
    {
        // Fixed aspect; just apply the host’s size (typically from scaling)
        int w = Math.Max(1, newSize.Right - newSize.Left);
        int h = Math.Max(1, newSize.Bottom - newSize.Top);
        _window.SetBounds(0, 0, w, h);
    }

    public void OnFocus(bool state)
    {
        // no-op
    }

    public void SetFrame(IAudioPluginFrame frame)
    {
        _frame = frame;
    }

    public bool CanResize() => false; // fixed-size editor

    public bool CheckSizeConstraint(ref ViewRectangle rect)
    {
        // force to our fixed (scaled) size
        int w = (int)Math.Round(BaseWidth * _scale);
        int h = (int)Math.Round(BaseHeight * _scale);
        rect = new ViewRectangle(rect.Left, rect.Top, rect.Left + w, rect.Top + h);
        return true;
    }

    public void SetContentScaleFactor(float factor)
    {
        _scale = Math.Clamp(factor, 0.5f, 4.0f);
        // ask host to resize us to the new scaled size
        _frame?.ResizeView(this, Size);
    }

    public bool TryFindParameter(int xPos, int yPos, out AudioParameterId parameterId)
    {
        // simple: always map to Gain (good enough for a 1-control editor)
        parameterId = _model.Gain.Id;
        return true;
    }

    // --- helpers ---

    private void AdjustDb(double deltaDb)
    {
        const double minDb = -60.0, maxDb = 12.0;

        var norm = (double)_model.Gain.NormalizedValue;
        var db = Math.Clamp(minDb + norm * (maxDb - minDb) + deltaDb, minDb, maxDb);
        var newNorm = (float)((db - minDb) / (maxDb - minDb));

        var p = _model.Gain;
        _controller.BeginEditParameter(p);
        p.NormalizedValue = newNorm;
        _controller.EndEditParameter();

        _window.RefreshLabel();
    }
}
