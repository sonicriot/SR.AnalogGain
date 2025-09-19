using NPlug;
using Xunit;
using Xunit.Abstractions;
using Moq;

namespace SR.AnalogGain.Tests;

/// <summary>
/// Tests for basic component creation and initialization
/// </summary>
public class ComponentCreationTests
{
    private readonly ITestOutputHelper _output;

    public ComponentCreationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Controller_ShouldCreateSuccessfully()
    {
        // Arrange & Act
        var controller = new AnalogGainController();

        // Assert
        Assert.NotNull(controller);
        Assert.NotNull(controller.Model);

        _output.WriteLine($"Controller created with ClassId: {AnalogGainController.ClassId}");
    }

    [Fact]
    public void Processor_ShouldCreateSuccessfully()
    {
        // Arrange & Act
        var processor = new AnalogGainProcessor();

        // Assert
        Assert.NotNull(processor);
        Assert.Equal(AnalogGainController.ClassId, processor.ControllerClassId);
        Assert.NotNull(processor.Model);
        
        _output.WriteLine($"Processor created with ClassId: {AnalogGainController.ClassId}");
        _output.WriteLine($"Controller ClassId: {processor.ControllerClassId}");
    }

    [Fact]
    public void Controller_ShouldCreateView()
    {
        // Arrange
        var controller = new AnalogGainController();

        var view = ((IAudioController)controller).CreateView();

        // Assert
        Assert.NotNull(view);
        Assert.IsType<AnalogGainEditor>(view);
        
        var editor = view as AnalogGainEditor;
        Assert.NotNull(editor);
        
        _output.WriteLine($"View created successfully: {editor.GetType().Name}");
    }

    [Fact]
    public void MultipleControllers_ShouldBeIndependent()
    {
        // Arrange & Act
        var controller1 = new AnalogGainController();
        var controller2 = new AnalogGainController();

        // Mock handler
        var handler1 = new Mock<IAudioControllerHandler>();
        var handler2 = new Mock<IAudioControllerHandler>();

        ((IAudioController)controller1).SetControllerHandler(handler1.Object);
        ((IAudioController)controller2).SetControllerHandler(handler2.Object);

        // Assert
        Assert.NotNull(controller1);
        Assert.NotNull(controller2);
        Assert.NotSame(controller1, controller2);
        Assert.NotSame(controller1.Model, controller2.Model);

        // Test parameter independence
        controller1.BeginEditParameter(controller1.Model.Gain);
        controller1.Model.Gain.NormalizedValue = 0.3;
        controller1.EndEditParameter();
        controller2.BeginEditParameter(controller2.Model.Gain);
        controller2.Model.Gain.NormalizedValue = 0.7;
        controller2.EndEditParameter();

        Assert.NotEqual(controller1.Model.Gain.NormalizedValue, controller2.Model.Gain.NormalizedValue);

        _output.WriteLine($"Controller 1 gain: {controller1.Model.Gain.NormalizedValue:F3}");
        _output.WriteLine($"Controller 2 gain: {controller2.Model.Gain.NormalizedValue:F3}");
    }

    [Fact]
    public void MultipleProcessors_ShouldBeIndependent()
    {
        // Arrange & Act
        var processor1 = new AnalogGainProcessor();
        var processor2 = new AnalogGainProcessor();

        // Assert
        Assert.NotNull(processor1);
        Assert.NotNull(processor2);
        Assert.NotSame(processor1, processor2);
        Assert.NotSame(processor1.Model, processor2.Model);
        
        // Test parameter independence
        processor1.Model.Gain.NormalizedValue = 0.2;
        processor2.Model.Gain.NormalizedValue = 0.8;
        
        Assert.NotEqual(processor1.Model.Gain.NormalizedValue, processor2.Model.Gain.NormalizedValue);
        
        _output.WriteLine($"Processor 1 gain: {processor1.Model.Gain.NormalizedValue:F3}");
        _output.WriteLine($"Processor 2 gain: {processor2.Model.Gain.NormalizedValue:F3}");
    }

    [Fact]
    public void ClassIds_ShouldBeConsistent()
    {
        // Arrange
        var controller = new AnalogGainController();
        var processor = new AnalogGainProcessor();

        // Act & Assert
        Assert.Equal(AnalogGainController.ClassId, processor.ControllerClassId);
        
        // ClassIds should be different between controller and processor
        Assert.NotEqual(AnalogGainController.ClassId, AnalogGainProcessor.ClassId);
        
        _output.WriteLine($"Controller ClassId: {AnalogGainController.ClassId}");
        _output.WriteLine($"Processor ClassId: {AnalogGainProcessor.ClassId}");
        _output.WriteLine($"Processor->Controller ClassId: {processor.ControllerClassId}");
    }
}
