using System;
using Xunit;
using Xunit.Abstractions;

namespace SR.AnalogGain.Tests;

/// <summary>
/// Tests for mathematical accuracy of dB calculations and parameter mappings
/// </summary>
public class MathValidationTests
{
    private readonly ITestOutputHelper _output;

    public MathValidationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(-60.0, 0.001)]      // -60dB ≈ 0.001 linear
    [InlineData(-20.0, 0.1)]        // -20dB = 0.1 linear
    [InlineData(-6.0, 0.501)]       // -6dB ≈ 0.5 linear
    [InlineData(0.0, 1.0)]          // 0dB = 1.0 linear
    [InlineData(6.0, 1.995)]        // +6dB ≈ 2.0 linear
    [InlineData(12.0, 3.981)]       // +12dB ≈ 4.0 linear
    public void DbToLinearConversion_ShouldBeAccurate(double dB, double expectedLinear)
    {
        // Act
        double actualLinear = Math.Pow(10.0, dB / 20.0);

        // Assert
        Assert.Equal(expectedLinear, actualLinear, precision: 2);
        
        _output.WriteLine($"{dB:F1}dB -> {actualLinear:F3} linear (expected {expectedLinear:F3})");
    }

    [Theory]
    [InlineData(0.0, -60.0)]        // Min gain
    [InlineData(0.5, -24.0)]        // Mid gain
    [InlineData(0.833333, 0.0)]     // 0dB gain
    [InlineData(1.0, 12.0)]         // Max gain
    public void GainParameterMapping_ShouldBeAccurate(double normalizedValue, double expectedDb)
    {
        // Arrange
        const double minDb = -60.0;
        const double maxDb = 12.0;

        // Act
        double actualDb = minDb + normalizedValue * (maxDb - minDb);

        // Assert
        Assert.Equal(expectedDb, actualDb, precision: 1);
        
        _output.WriteLine($"Gain normalized {normalizedValue:F6} -> {actualDb:F2}dB (expected {expectedDb:F2}dB)");
    }

    [Theory]
    [InlineData(0.0, -24.0)]        // Min output
    [InlineData(0.5, -6.0)]         // Mid output
    [InlineData(0.666667, 0.0)]     // 0dB output
    [InlineData(1.0, 12.0)]         // Max output
    public void OutputParameterMapping_ShouldBeAccurate(double normalizedValue, double expectedDb)
    {
        // Arrange
        const double minDb = -24.0;
        const double maxDb = 12.0;

        // Act
        double actualDb = minDb + normalizedValue * (maxDb - minDb);

        // Assert
        Assert.Equal(expectedDb, actualDb, precision: 1);
        
        _output.WriteLine($"Output normalized {normalizedValue:F6} -> {actualDb:F2}dB (expected {expectedDb:F2}dB)");
    }

    [Fact]
    public void GainParameter_DefaultShouldBe0dB()
    {
        // Arrange
        var model = new AnalogGainModel();
        const double minDb = -60.0;
        const double maxDb = 12.0;

        // Act
        double actualDb = minDb + model.Gain.NormalizedValue * (maxDb - minDb);

        // Assert
        Assert.Equal(0.0, actualDb, precision: 1);
        
        _output.WriteLine($"Gain default: normalized {model.Gain.NormalizedValue:F6} = {actualDb:F2}dB");
    }

    [Fact]
    public void OutputParameter_DefaultShouldBe0dB()
    {
        // Arrange
        var model = new AnalogGainModel();
        const double minDb = -24.0;
        const double maxDb = 12.0;

        // Act
        double actualDb = minDb + model.Output.NormalizedValue * (maxDb - minDb);

        // Assert
        Assert.Equal(0.0, actualDb, precision: 1);
        
        _output.WriteLine($"Output default: normalized {model.Output.NormalizedValue:F6} = {actualDb:F2}dB");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void ParameterRoundTrip_ShouldBeConsistent(double originalNormalized)
    {
        // Arrange
        var model = new AnalogGainModel();

        // Act - Set and get back the value
        model.Gain.NormalizedValue = originalNormalized;
        double retrievedNormalized = model.Gain.NormalizedValue;

        // Assert
        Assert.Equal(originalNormalized, retrievedNormalized, precision: 6);
        
        _output.WriteLine($"Round trip: {originalNormalized:F6} -> {retrievedNormalized:F6}");
    }

    [Fact]
    public void CombinedGainCalculation_ShouldBeCorrect()
    {
        // Test the combined effect of gain and output parameters
        
        // Arrange
        var model = new AnalogGainModel();
        model.Gain.NormalizedValue = 1.0;      // +12dB gain
        model.Output.NormalizedValue = 0.5;    // -6dB output
        
        // Act - Calculate combined effect
        const double gainMinDb = -60.0, gainMaxDb = 12.0;
        const double outputMinDb = -24.0, outputMaxDb = 12.0;
        
        double gainDb = gainMinDb + model.Gain.NormalizedValue * (gainMaxDb - gainMinDb);
        double outputDb = outputMinDb + model.Output.NormalizedValue * (outputMaxDb - outputMinDb);
        double combinedDb = gainDb + outputDb;
        
        double gainLinear = Math.Pow(10.0, gainDb / 20.0);
        double outputLinear = Math.Pow(10.0, outputDb / 20.0);
        double combinedLinear = gainLinear * outputLinear;
        
        // Assert
        Assert.Equal(12.0, gainDb, precision: 1);
        Assert.Equal(-6.0, outputDb, precision: 1);
        Assert.Equal(6.0, combinedDb, precision: 1);
        
        _output.WriteLine($"Gain: {gainDb:F1}dB (linear {gainLinear:F3})");
        _output.WriteLine($"Output: {outputDb:F1}dB (linear {outputLinear:F3})");
        _output.WriteLine($"Combined: {combinedDb:F1}dB (linear {combinedLinear:F3})");
    }
}
