using System;
using System.Runtime.InteropServices;
using NPlug;

namespace NPlug.SimpleGain;

/// <summary>
/// Simple, fixed-aspect editor that hosts a Win32 knob window and negotiates size with the host.
/// DPI- and host-scale aware, with an extra user scale to globally shrink/enlarge the UI.
/// </summary>
public sealed class SimpleGainEditor : IAudioPluginView
{
    // ---- Dependencies -------------------------------------------------------

    private readonly SimpleGainController _controller;
    private readonly SimpleGainModel _model;
    private readonly UI.Win32.FixedGainWindow _window;

    // ---- Host frame / scaling ----------------------------------------------

    private IAudioPluginFrame? _frame;
    private float _scale = 1.0f; // final content scale = hostScale * UserUiScale

    // Base logical size (in DIP, before scaling)
    private const int BaseWidth = 512;
    private const int BaseHeight = 512;

    // Global user multiplier applied on top of host DPI/content scale (tune once here)
    private const float UserUiScale = 0.50f;

    // Clamp for SetContentScaleFactor
    private const float MinScale = 0.5f;
    private const float MaxScale = 4.0f;

    // ---- Ctor ---------------------------------------------------------------

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
            beginEdit: () => _controller.BeginEditParameter(_model.Gain),
            performEdit: nv =>
            {
                // Update parameter (controller will broadcast changes)
                _model.Gain.NormalizedValue = (float)Math.Clamp(nv, 0.0, 1.0);
            },
            endEdit: () => _controller.EndEditParameter()
        );
    }

    // ---- IAudioPluginView ---------------------------------------------------

    public bool IsPlatformTypeSupported(AudioPluginViewPlatform platform)
        => platform == AudioPluginViewPlatform.Hwnd;

    public void Attached(nint parent, AudioPluginViewPlatform type)
    {
        if (type != AudioPluginViewPlatform.Hwnd) return;

        _window.AttachToParent(parent);

        // Determine initial scale (host DPI/content scale) and apply user multiplier
        _scale = DetermineInitialScale(parent) * UserUiScale;

        // Ask host to resize the view to our scaled size and layout the child window
        ApplyScaleAndResize();
    }

    public void Removed() => _window.Destroy();

    public void OnWheel(float distance)
    {
        // Wheel: +/- 0.5 dB per notch
        AdjustDb(Math.Sign(distance) * 0.5);
    }

    public void OnKeyDown(ushort key, short keyCode, short modifiers)
    {
        // Left/Right to nudge +/- 0.5 dB
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
            var (w, h) = GetScaledSize();
            return new ViewRectangle(0, 0, w, h);
        }
    }

    public void OnSize(ViewRectangle newSize)
    {
        // Host-driven size change (usually after scale negotiation): just fit the child
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

        // Ensure host gets our current preferred size when frame arrives after Attached
        ApplyScaleAndResize();
    }

    // Keep this true so ResizeView is honored; CheckSizeConstraint enforces fixed size.
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
        // Single-control editor: always map hits to Gain.
        parameterId = _model.Gain.Id;
        return true;
    }

    // ---- Helpers ------------------------------------------------------------

    /// <summary>
    /// Adjusts the gain parameter by a dB delta, clamps to range, and refreshes UI.
    /// </summary>
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

        _window.RefreshUI();
    }

    /// <summary>
    /// Returns the current scaled size in pixels (host units), applying _scale.
    /// </summary>
    private (int w, int h) GetScaledSize()
    {
        int w = (int)Math.Round(BaseWidth * _scale);
        int h = (int)Math.Round(BaseHeight * _scale);
        return (w, h);
    }

    /// <summary>
    /// Negotiates size with the host and lays out the child window accordingly.
    /// </summary>
    private void ApplyScaleAndResize()
    {
        var (w, h) = GetScaledSize();
        var rect = new ViewRectangle(0, 0, w, h);

        // Ask host to resize the editor if possible
        _frame?.ResizeView(this, rect);

        // Make sure our child window matches whatever the host will use
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
            // Query the current process DPI awareness (use REAL process handle)
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

    // ---- Native DPI helpers -------------------------------------------------

    // Prefer safe wrappers in case API is missing (older OS) or returns 0.

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
