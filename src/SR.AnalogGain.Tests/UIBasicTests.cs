using Moq;
using NPlug;
using Xunit;
using Xunit.Abstractions;

namespace SR.AnalogGain.Tests;

/// <summary>
/// Basic UI tests that don't require Win32 integration
/// </summary>
public class UIBasicTests
{
    private readonly ITestOutputHelper _output;

    public UIBasicTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Editor_ShouldHaveValidSize()
    {
        // Arrange
        var controller = new AnalogGainController();
        var handler = new Mock<IAudioControllerHandler>();
        var iAudionController = (IAudioController)controller;
        iAudionController.SetControllerHandler(handler.Object);
        var view = iAudionController.CreateView();
        var editor = view as AnalogGainEditor;

        // Act & Assert
        Assert.NotNull(editor);
        
        var size = editor.Size;
        Assert.True(size.Right > size.Left, "Editor width should be positive");
        Assert.True(size.Bottom > size.Top, "Editor height should be positive");
        
        int width = size.Right - size.Left;
        int height = size.Bottom - size.Top;
        
        Assert.True(width > 0 && width < 5000, "Width should be reasonable");
        Assert.True(height > 0 && height < 5000, "Height should be reasonable");
        
        _output.WriteLine($"Editor size: {width}x{height}");
    }

    [Fact]
    public void Editor_ShouldSupportPlatform()
    {
        // Arrange
        var controller = new AnalogGainController();
        var handler = new Mock<IAudioControllerHandler>();
        var iAudionController = (IAudioController)controller;
        iAudionController.SetControllerHandler(handler.Object);
        var view = iAudionController.CreateView();
        var editor = view as AnalogGainEditor;

        // Act & Assert
        Assert.NotNull(editor);
        
        // Should support HWND platform (Windows)
        bool supportsHwnd = editor.IsPlatformTypeSupported(NPlug.AudioPluginViewPlatform.Hwnd);
        Assert.True(supportsHwnd, "Should support HWND platform on Windows");
        
        _output.WriteLine($"Supports HWND platform: {supportsHwnd}");
    }

    [Fact]
    public void Editor_ShouldHandleKeyboardEvents()
    {
        // Arrange
        var controller = new AnalogGainController();
        var handler = new Mock<IAudioControllerHandler>();
        var iAudionController = (IAudioController)controller;
        iAudionController.SetControllerHandler(handler.Object);
        var view = iAudionController.CreateView();
        var editor = view as AnalogGainEditor;

        // Act & Assert
        Assert.NotNull(editor);
        
        // These should not throw exceptions
        var exception = Record.Exception(() =>
        {
            editor.OnKeyDown(32, 0, 0); // Spacebar
            editor.OnKeyUp(32, 0, 0);   // Spacebar
            editor.OnKeyDown(13, 0, 0); // Enter
            editor.OnKeyUp(13, 0, 0);   // Enter
        });

        Assert.Null(exception);
        _output.WriteLine("Keyboard events handled without exceptions");
    }

    [Fact]
    public void Editor_ShouldHandleMouseWheel()
    {
        // Arrange
        var controller = new AnalogGainController();
        var handler = new Mock<IAudioControllerHandler>();
        var iAudionController = (IAudioController)controller;
        iAudionController.SetControllerHandler(handler.Object);

        var view = iAudionController.CreateView();
        var editor = view as AnalogGainEditor;

        // Act & Assert
        Assert.NotNull(editor);
        
        var initialGainValue = controller.Model.Gain.NormalizedValue;
        
        // Test mouse wheel events
        var exception = Record.Exception(() =>
        {
            editor.OnWheel(1.0f);   // Scroll up
            editor.OnWheel(-1.0f);  // Scroll down
            editor.OnWheel(0.0f);   // No scroll
        });

        Assert.Null(exception);
        _output.WriteLine($"Mouse wheel events handled - Initial gain: {initialGainValue:F6}");
    }

    [Fact]
    public void Editor_ShouldHandleFocusEvents()
    {
        // Arrange
        var controller = new AnalogGainController();
        var handler = new Mock<IAudioControllerHandler>();
        var iAudionController = (IAudioController)controller;
        iAudionController.SetControllerHandler(handler.Object);
        var view = iAudionController.CreateView();
        var editor = view as AnalogGainEditor;

        // Act & Assert
        Assert.NotNull(editor);
        
        var exception = Record.Exception(() =>
        {
            editor.OnFocus(true);   // Gain focus
            editor.OnFocus(false);  // Lose focus
        });

        Assert.Null(exception);
        _output.WriteLine("Focus events handled without exceptions");
    }

    [Fact]
    public void Editor_ShouldRefreshUI()
    {
        // Arrange
        var controller = new AnalogGainController();
        var handler = new Mock<IAudioControllerHandler>();
        var iAudionController = (IAudioController)controller;
        iAudionController.SetControllerHandler(handler.Object);
        var view = iAudionController.CreateView();
        var editor = view as AnalogGainEditor;

        // Act & Assert
        Assert.NotNull(editor);

        // Change parameters and refresh UI
        controller.BeginEditParameter(controller.Model.Gain);
        controller.Model.Gain.NormalizedValue = 0.5;
        controller.EndEditParameter();
        controller.BeginEditParameter(controller.Model.Output);
        controller.Model.Output.NormalizedValue = 0.3;
        controller.EndEditParameter();

        var exception = Record.Exception(() =>
        {
            editor.RefreshUI();
            editor.ForceUIUpdate();
        });

        Assert.Null(exception);
        _output.WriteLine("UI refresh methods executed without exceptions");
    }

    [Fact]
    public void Editor_CanResize_ShouldReturnTrue()
    {
        // Arrange
        var controller = new AnalogGainController();
        var handler = new Mock<IAudioControllerHandler>();
        var iAudionController = (IAudioController)controller;
        iAudionController.SetControllerHandler(handler.Object);
        var view = iAudionController.CreateView();
        var editor = view as AnalogGainEditor;

        // Act & Assert
        Assert.NotNull(editor);
        Assert.True(editor.CanResize(), "Editor should support resizing");
        
        _output.WriteLine("Editor supports resizing");
    }

    [Fact]
    public void Editor_ShouldHandleParameterHitTesting()
    {
        // Arrange
        var controller = new AnalogGainController();
        var handler = new Mock<IAudioControllerHandler>();
        var iAudionController = (IAudioController)controller;
        iAudionController.SetControllerHandler(handler.Object);
        var view = iAudionController.CreateView();
        var editor = view as AnalogGainEditor;

        // Act & Assert
        Assert.NotNull(editor);
        
        var size = editor.Size;
        int centerX = (size.Right - size.Left) / 2;
        int centerY = (size.Bottom - size.Top) / 2;
        
        // Test parameter hit testing
        bool foundParameter = editor.TryFindParameter(centerX, centerY, out var parameterId);
        
        if (foundParameter)
        {
            _output.WriteLine($"Found parameter at center: {parameterId.Value}");
        }
        else
        {
            _output.WriteLine("No parameter found at center position");
        }
        
        // Should not throw regardless of result
        Assert.True(true, "Hit testing completed without exceptions");
    }
}
