using System.Runtime.CompilerServices;

namespace NPlug.SimpleGain;

public static class SimplegainPlugin
{
    public static AudioPluginFactory GetFactory()
    {
        var factory = new AudioPluginFactory(new("NPlug", "https://github.com/xoofx/NPlug", "no_reply@nplug.org"));
        factory.RegisterPlugin<SimpleGainProcessor>(new(SimpleGainProcessor.ClassId, "SimpleGain", AudioProcessorCategory.Effect));
        factory.RegisterPlugin<SimpleGainController>(new(SimpleGainController.ClassId, "SimpleGain Controller"));
        return factory;
    }

    [ModuleInitializer]
    internal static void ExportThisPlugin()
    {
        AudioPluginFactoryExporter.Instance = GetFactory();
    }
}
