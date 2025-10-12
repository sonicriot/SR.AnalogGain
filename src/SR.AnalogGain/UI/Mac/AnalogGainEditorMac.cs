#if MACOS
using NPlug;
using System;

namespace SR.AnalogGain.UI.Mac
{
    /// <summary>
    /// macOS editor that hosts a Cocoa/CoreGraphics dual-knob view (GAIN + OUTPUT)
    /// with 4 switches (SAT, PAD, PHASE, HPF). DPI/content-scale aware.
    /// </summary>
    public sealed class AnalogGainEditorMac : IAnalogGainEditorView
    {
        private readonly AnalogGainController _controller;
        private readonly AnalogGainModel _model;
        private readonly MacFixedDualKnobView _window;

        private IAudioPluginFrame? _frame;
        private float _scale = 1.0f;

        private const int BaseWidth  = 1024;
        private const int BaseHeight = 512;

        private const float UserUiScale = 0.50f;
        private const float MinScale = 0.5f;
        private const float MaxScale = 4.0f;

        // ------------------------------------------------------------
        public AnalogGainEditorMac(AnalogGainController controller, AnalogGainModel model)
        {
            _controller = controller;
            _model = model;

            _window = new MacFixedDualKnobView(
                // GAIN knob (left)
                () => (float)_model.Gain.NormalizedValue,
                () => _controller.BeginEditParameter(_model.Gain),
                nv => _model.Gain.NormalizedValue = (float)Math.Clamp(nv, 0.0, 1.0),
                () => _controller.EndEditParameter(),
                -60.0, +12.0,

                // OUTPUT knob (right)
                () => (float)_model.Output.NormalizedValue,
                () => _controller.BeginEditParameter(_model.Output),
                nv => _model.Output.NormalizedValue = (float)Math.Clamp(nv, 0.0, 1.0),
                () => _controller.EndEditParameter(),
                -24.0, +12.0,

                // SAT
                () => _model.Sat.Value,
                () => _controller.BeginEditParameter(_model.Sat),
                v => _model.Sat.Value = v,
                () => _controller.EndEditParameter(),

                // PAD
                () => _model.Pad.Value,
                () => _controller.BeginEditParameter(_model.Pad),
                v => _model.Pad.Value = v,
                () => _controller.EndEditParameter(),

                // PHASE
                () => _model.Phase.Value,
                () => _controller.BeginEditParameter(_model.Phase),
                v => _model.Phase.Value = v,
                () => _controller.EndEditParameter(),

                // HPF
                () => _model.Hpf.Value,
                () => _controller.BeginEditParameter(_model.Hpf),
                v => _model.Hpf.Value = v,
                () => _controller.EndEditParameter()
            );
        }

        // ------------------------------------------------------------
        // IAudioPluginView implementation
        // ------------------------------------------------------------

        public bool IsPlatformTypeSupported(AudioPluginViewPlatform platform)
            => platform == AudioPluginViewPlatform.NSView;

        public void Attached(nint parent, AudioPluginViewPlatform type)
        {
            if (type != AudioPluginViewPlatform.NSView) return;

            _window.AttachToParent(parent);

            // Base user scale (host usually provides SetContentScaleFactor later)
            _scale = Math.Clamp(1.0f * UserUiScale, MinScale, MaxScale);

            ApplyScaleAndResize();
            _window.Refresh();
        }

        public void Removed()
        {
            _window.Destroy();
            if (_controller is AnalogGainController ctrl)
                ctrl.OnEditorClosed();
        }

        public void OnWheel(float distance)
        {
            // Optional: tweak GAIN knob with scroll wheel
            AdjustGainDb(Math.Sign(distance) * 0.5);
        }

        public void OnKeyDown(ushort key, short keyCode, short modifiers) { }
        public void OnKeyUp(ushort key, short keyCode, short modifiers) { }

        public ViewRectangle Size
        {
            get
            {
                var (w, h) = GetScaledSize();
                return new ViewRectangle(0, 0, w, h);
            }
        }

        public void OnSize(ViewRectangle newSize)
        {
            int w = Math.Max(1, newSize.Right - newSize.Left);
            int h = Math.Max(1, newSize.Bottom - newSize.Top);
            _window.SetBounds(0, 0, w, h);
        }

        public void OnFocus(bool state) { }

        public void SetFrame(IAudioPluginFrame frame)
        {
            _frame = frame;
            ApplyScaleAndResize();
        }

        public bool CanResize() => true;

        public bool CheckSizeConstraint(ref ViewRectangle rect)
        {
            var (w, h) = GetScaledSize();
            rect = new ViewRectangle(rect.Left, rect.Top, rect.Left + w, rect.Top + h);
            return true;
        }

        public void SetContentScaleFactor(float factor)
        {
            _scale = Math.Clamp(factor * UserUiScale, MinScale, MaxScale);
            ApplyScaleAndResize();
        }

        public bool TryFindParameter(int xPos, int yPos, out AudioParameterId parameterId)
        {
            var (w, _) = GetScaledSize();
            parameterId = (xPos < w / 2) ? _model.Gain.Id : _model.Output.Id;
            return true;
        }

        // ------------------------------------------------------------
        // Public UI helpers
        // ------------------------------------------------------------
        public void RefreshUI() => _window.Refresh();
        public void ForceUIUpdate() { _window.Refresh(); ApplyScaleAndResize(); }
        public void RefreshParameter(AudioParameter parameter) => _window.Refresh();

        // ------------------------------------------------------------
        // Internal helpers
        // ------------------------------------------------------------
        private void AdjustGainDb(double deltaDb)
        {
            const double minDb = -60.0, maxDb = 12.0;

            var norm = (double)_model.Gain.NormalizedValue;
            var db = Math.Clamp(minDb + norm * (maxDb - minDb) + deltaDb, minDb, maxDb);
            var newNorm = (float)((db - minDb) / (maxDb - minDb));

            var p = _model.Gain;
            _controller.BeginEditParameter(p);
            p.NormalizedValue = newNorm;
            _controller.EndEditParameter();

            _window.Refresh();
        }

        private (int w, int h) GetScaledSize()
        {
            int w = (int)Math.Round(BaseWidth * _scale);
            int h = (int)Math.Round(BaseHeight * _scale);
            return (w, h);
        }

        private void ApplyScaleAndResize()
        {
            var (w, h) = GetScaledSize();
            var rect = new ViewRectangle(0, 0, w, h);

            _frame?.ResizeView(this, rect);
            _window.SetBounds(0, 0, w, h);
            _window.Refresh();
        }
    }
}
#endif
