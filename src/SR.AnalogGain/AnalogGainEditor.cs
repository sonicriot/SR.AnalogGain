using System;
using System.Runtime.InteropServices;
using NPlug;

namespace SR.AnalogGain
{
    /// <summary>
    /// Fixed-aspect editor that hosts a Win32 dual-knob container (GAIN + OUTPUT).
    /// DPI- and host-scale aware, with an extra user scale to globally shrink/enlarge the UI.
    /// </summary>
    public sealed class AnalogGainEditor : IAudioPluginView
    {
        // ---- Dependencies ---------------------------------------------------
        private readonly AnalogGainController _controller;
        private readonly AnalogGainModel _model;
        private readonly UI.Win32.FixedDualKnobWindow _window;

        // ---- Host frame / scaling ------------------------------------------
        private IAudioPluginFrame? _frame;
        private float _scale = 1.0f; // final content scale = hostScale * UserUiScale

        // Base logical size (DIP, before scaling)
        private const int BaseWidth = 1024;
        private const int BaseHeight = 512;

        // Extra user multiplier applied on top of host DPI/content scale
        private const float UserUiScale = 0.50f;

        // Clamp for SetContentScaleFactor
        private const float MinScale = 0.5f;
        private const float MaxScale = 4.0f;

        // ---- Ctor -----------------------------------------------------------
        public AnalogGainEditor(AnalogGainController controller, AnalogGainModel model)
        {
            _controller = controller;
            _model = model;

            // Match FixedDualKnobWindow signature (left = GAIN, right = OUTPUT, LO-Z)
            _window = new UI.Win32.FixedDualKnobWindow(
                // Left knob (GAIN)
                getNormalizedLeft: () => (float)_model.Gain.NormalizedValue,
                beginLeft: () => _controller.BeginEditParameter(_model.Gain),
                performLeft: nv => _model.Gain.NormalizedValue = (float)Math.Clamp(nv, 0.0, 1.0),
                endLeft: () => _controller.EndEditParameter(),
                minDbLeft: -60.0, maxDbLeft: +12.0,

                // Right knob (OUTPUT)
                getNormalizedRight: () => (float)_model.Output.NormalizedValue,
                beginRight: () => _controller.BeginEditParameter(_model.Output),
                performRight: nv => _model.Output.NormalizedValue = (float)Math.Clamp(nv, 0.0, 1.0),
                endRight: () => _controller.EndEditParameter(),
                minDbRight: -24.0, maxDbRight: +12.0,

                // LO-Z switch
                getLoZ: () => _model.LoZ.Value,
                beginLoZ: () => _controller.BeginEditParameter(_model.LoZ),
                performLoZ: v => _model.LoZ.Value = v,
                endLoZ: () => _controller.EndEditParameter(),

                // PAD switch (new)
                getPad: () => _model.Pad.Value,
                beginPad: () => _controller.BeginEditParameter(_model.Pad),
                performPad: v => _model.Pad.Value = v,
                endPad: () => _controller.EndEditParameter(),

                // PHASE switch (new)
                getPhase: () => _model.Phase.Value,
                beginPhase: () => _controller.BeginEditParameter(_model.Phase),
                performPhase: v => _model.Phase.Value = v,
                endPhase: () => _controller.EndEditParameter(),

                // HPF switch (new)
                getHpf: () => _model.Hpf.Value,
                beginHpf: () => _controller.BeginEditParameter(_model.Hpf),
                performHpf: v => _model.Hpf.Value = v,
                endHpf: () => _controller.EndEditParameter()
            );
        }

        // ---- IAudioPluginView ----------------------------------------------
        public bool IsPlatformTypeSupported(AudioPluginViewPlatform platform)
            => platform == AudioPluginViewPlatform.Hwnd;

        public void Attached(nint parent, AudioPluginViewPlatform type)
        {
            if (type != AudioPluginViewPlatform.Hwnd) return;

            _window.AttachToParent(parent);

            // Determine initial scale (host DPI/content scale) and apply user multiplier
            _scale = DetermineInitialScale(parent) * UserUiScale;

            // Ask host to resize and layout the child window
            ApplyScaleAndResize();
            
            // Refresh UI to ensure knobs reflect current parameter values
            // (important when editor is reopened after host preset changes)
            _window.RefreshUI();
        }

        public void Removed() 
        {
            _window.Destroy();
            // Notify controller that editor is closed
            if (_controller is AnalogGainController controller)
                controller.OnEditorClosed();
        }

        public void OnWheel(float distance)
        {
            // Simple nudge on GAIN with mouse wheel (optional)
            AdjustGainDb(Math.Sign(distance) * 0.5);
        }

        public void OnKeyDown(ushort key, short keyCode, short modifiers) { /* no-op */ }

        public void OnKeyUp(ushort key, short keyCode, short modifiers) { /* no-op */ }

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
            // Host-driven size change: fit the child
            int w = Math.Max(1, newSize.Right - newSize.Left);
            int h = Math.Max(1, newSize.Bottom - newSize.Top);
            _window.SetBounds(0, 0, w, h);
        }

        public void OnFocus(bool state) { /* no-op */ }

        public void SetFrame(IAudioPluginFrame frame)
        {
            _frame = frame;
            // Ensure host gets our preferred size if frame arrives after Attached
            ApplyScaleAndResize();
        }

        // Keep true so ResizeView is honored; CheckSizeConstraint enforces fixed size.
        public bool CanResize() => true;

        public bool CheckSizeConstraint(ref ViewRectangle rect)
        {
            // Force the host to our fixed, scaled size
            var (w, h) = GetScaledSize();
            rect = new ViewRectangle(rect.Left, rect.Top, rect.Left + w, rect.Top + h);
            return true;
        }

        public void SetContentScaleFactor(float factor)
        {
            // Host-provided content scale (e.g., DPI). Apply user multiplier and clamp.
            _scale = Math.Clamp(factor * UserUiScale, MinScale, MaxScale);
            ApplyScaleAndResize();
        }

        public bool TryFindParameter(int xPos, int yPos, out AudioParameterId parameterId)
        {
            // Map hit testing to left/right half (GAIN/OUTPUT)
            var (w, _) = GetScaledSize();
            parameterId = (xPos < w / 2) ? _model.Gain.Id : _model.Output.Id;
            return true;
        }

        // ---- Public Methods -------------------------------------------------
        
        /// <summary>
        /// Refreshes the UI to reflect current parameter values.
        /// Called when parameters are changed externally (e.g., host preset loading, automation).
        /// </summary>
        public void RefreshUI()
        {
            _window.RefreshUI();
        }

        /// <summary>
        /// Forces an immediate UI update by checking current parameter values.
        /// This is a more aggressive refresh that ensures the UI matches the model.
        /// </summary>
        public void ForceUIUpdate()
        {
            // Force a complete UI refresh multiple times to ensure it takes effect
            _window.RefreshUI();
            _window.RefreshUI(); // Call twice to ensure it works
            
            // Also trigger a window repaint
            if (_window != null)
            {
                // This will force the knobs to re-read their values and redraw
                ApplyScaleAndResize();
            }
        }

        // ---- Helpers --------------------------------------------------------
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

            _window.RefreshUI();
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

            // Ask host to resize the editor if possible
            _frame?.ResizeView(this, rect);

            // Ensure our child window matches whatever the host will use
            _window.SetBounds(0, 0, w, h);
            _window.RefreshUI();
        }

        /// <summary>
        /// Determines the initial host/DPI scale. If the process is DPI-unaware, compensate
        /// for Windows DPI virtualization; if it's DPI-aware, use per-window DPI.
        /// </summary>
        private float DetermineInitialScale(nint parent)
        {
            try
            {
                GetProcessDpiAwareness(GetCurrentProcess(), out int awareness);

                if (awareness == 0) // DPI_AWARENESS_UNAWARE → compensate virtualization
                {
                    int sys = SafeGetDpiForSystem();
                    return 96f / sys; // e.g., 96/144 = 0.666.. (draw smaller, Windows scales up)
                }
                else
                {
                    int dpi = SafeGetDpiForWindow(parent);
                    return dpi / 96f; // e.g., 144/96 = 1.5
                }
            }
            catch
            {
                return 1.0f;
            }
        }

        // ---- Native DPI helpers --------------------------------------------
        private static int SafeGetDpiForWindow(nint hWnd)
        {
            try
            {
                int dpi = GetDpiForWindow(hWnd);
                return dpi > 0 ? dpi : 96;
            }
            catch { return 96; }
        }

        private static int SafeGetDpiForSystem()
        {
            try
            {
                int dpi = GetDpiForSystem();
                return dpi > 0 ? dpi : 96;
            }
            catch { return 96; }
        }

        // P/Invoke
        [DllImport("user32.dll")] private static extern int GetDpiForWindow(nint hWnd);
        [DllImport("user32.dll")] private static extern int GetDpiForSystem(); // Win10+
        [DllImport("shcore.dll")] private static extern int GetProcessDpiAwareness(nint hprocess, out int value);
        [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    }
}
