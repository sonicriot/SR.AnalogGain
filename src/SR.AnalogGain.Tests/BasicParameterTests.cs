using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace SR.AnalogGain.Tests;

/// <summary>
/// Basic parameter validation tests for SR.AnalogGain plugin
/// </summary>
public class BasicParameterTests
{
    private readonly ITestOutputHelper _output;

    public BasicParameterTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Model_ShouldHaveCorrectParameterCount()
    {
        // Arrange & Act
        var model = new AnalogGainModel();

        // Assert
        
        Assert.NotNull(model);
        Assert.True(model.LocalParameterCount >= 3, "Should have at least 3 parameters (Gain, Output, Bypass)");
        
        _output.WriteLine($"Model has {model.ParameterCount} parameters");
    }

    [Fact]
    public void GainParameter_ShouldHaveCorrectProperties()
    {
        // Arrange
        var model = new AnalogGainModel();

        // Act & Assert
        Assert.NotNull(model.Gain);
        Assert.Equal("Gain [-60 to +12 dB]", model.Gain.Title);
        Assert.Equal("dB", model.Gain.Units);
        
        // Default should be 0dB (normalized ~0.833)
        const double minDb = -60.0;
        const double maxDb = 12.0;
        double expectedNorm0dB = (0.0 - minDb) / (maxDb - minDb);
        
        Assert.Equal(expectedNorm0dB, model.Gain.NormalizedValue, precision: 3);
        
        _output.WriteLine($"Gain parameter: '{model.Gain.Title}', default normalized: {model.Gain.NormalizedValue:F6}");
    }

    [Fact]
    public void OutputParameter_ShouldHaveCorrectProperties()
    {
        // Arrange
        var model = new AnalogGainModel();

        // Act & Assert
        Assert.NotNull(model.Output);
        Assert.Equal("Output [-24 to +12 dB]", model.Output.Title);
        Assert.Equal("dB", model.Output.Units);
        
        // Default should be 0dB (normalized ~0.667)
        const double minDb = -24.0;
        const double maxDb = 12.0;
        double expectedNorm0dB = (0.0 - minDb) / (maxDb - minDb);
        
        Assert.Equal(expectedNorm0dB, model.Output.NormalizedValue, precision: 3);
        
        _output.WriteLine($"Output parameter: '{model.Output.Title}', default normalized: {model.Output.NormalizedValue:F6}");
    }

    [Theory]
    [InlineData(0.0)]    // Minimum
    [InlineData(0.5)]    // Middle
    [InlineData(1.0)]    // Maximum
    public void GainParameter_ShouldAcceptValidNormalizedValues(double normalizedValue)
    {
        // Arrange
        var model = new AnalogGainModel();

        // Act
        model.Gain.NormalizedValue = normalizedValue;

        // Assert
        Assert.Equal(normalizedValue, model.Gain.NormalizedValue, precision: 6);
        
        // Calculate corresponding dB value
        const double minDb = -60.0;
        const double maxDb = 12.0;
        double dbValue = minDb + normalizedValue * (maxDb - minDb);
        
        _output.WriteLine($"Normalized {normalizedValue:F1} -> {dbValue:F1}dB");
    }

    [Theory]
    [InlineData(0.0)]    // Minimum
    [InlineData(0.5)]    // Middle
    [InlineData(1.0)]    // Maximum
    public void OutputParameter_ShouldAcceptValidNormalizedValues(double normalizedValue)
    {
        // Arrange
        var model = new AnalogGainModel();

        // Act
        model.Output.NormalizedValue = normalizedValue;

        // Assert
        Assert.Equal(normalizedValue, model.Output.NormalizedValue, precision: 6);
        
        // Calculate corresponding dB value
        const double minDb = -24.0;
        const double maxDb = 12.0;
        double dbValue = minDb + normalizedValue * (maxDb - minDb);
        
        _output.WriteLine($"Normalized {normalizedValue:F1} -> {dbValue:F1}dB");
    }

    [Fact]
    public void Parameters_ShouldHaveUniqueIds()
    {
        // Arrange
        var model = new AnalogGainModel();

        // Act & Assert
        var parameterIds = new HashSet<int>();
        
        for ( var i = 0; i < model.LocalParameterCount; i++)
        {
            var parameter = model.GetLocalParameter(i);
            Assert.True(parameterIds.Add(parameter.Id.Value), 
                $"Parameter ID {parameter.Id.Value} is not unique");
        }
        
        _output.WriteLine($"All {parameterIds.Count} parameter IDs are unique");
    }
}
