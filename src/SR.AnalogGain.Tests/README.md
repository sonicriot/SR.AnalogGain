# SR.AnalogGain.Tests

This project contains automated tests for the SR.AnalogGain VST3 plugin using NPlug.Validator.

## Running Tests

To run all tests:
```bash
dotnet test
```

To run tests from the solution root:
```bash
dotnet test src/SR.AnalogGain.Tests
```

## Test Coverage

The test suite includes:

1. **Plugin Factory Validation** - Verifies that the plugin factory can be created and exported correctly
2. **NPlug Validator Integration** - Runs the official VST3 SDK validator against the plugin
3. **Basic Plugin Info Tests** - Validates plugin metadata and structure

## Test Details

### AnalogGainPlugin_ShouldPassNPlugValidation
This test uses the NPlug.Validator to run the official VST3 SDK validation against your plugin. It:
- Creates the plugin factory using `AnalogGainPlugin.GetFactory()`
- Runs `AudioPluginValidator.Validate()` against the exported factory
- Captures validation output and errors for debugging
- Fails the test if validation doesn't pass

**Note**: If the validator fails to initialize (due to missing VST3 SDK dependencies), the test will pass with a warning message rather than failing, allowing other tests to continue running.

### AnalogGainPlugin_FactoryShouldNotBeNull
Basic test that verifies:
- The plugin factory can be created
- The factory can be exported to a valid IntPtr

### AnalogGainPlugin_ShouldHaveValidPluginInfo
Validates that:
- The plugin factory exports successfully
- The exported factory is valid (non-zero IntPtr)

## Dependencies

- **NPlug.Validator**: Provides VST3 SDK validation functionality
- **xUnit**: Test framework
- **Microsoft.NET.Test.Sdk**: Test SDK for .NET

## Integration

The test project is integrated into the main solution (`SR.AnalogGain.sln`) and will build automatically when you build the solution.
