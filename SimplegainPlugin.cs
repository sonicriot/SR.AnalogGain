using System.Runtime.CompilerServices;

namespace NPlug.SimpleGain;

public static class SimplegainPlugin
{
    public static AudioPluginFactory GetFactory()
    {
        var factory = new AudioPluginFactory(new("Sonic Riot", "https://sonicriotstudio.wordpress.com/", "sonicriotstudio@gmail.com"));
        factory.RegisterPlugin<SimpleGainProcessor>(new(SimpleGainProcessor.ClassId, "SimpleGain", AudioProcessorCategory.EffectDynamics));
        factory.RegisterPlugin<SimpleGainController>(new(SimpleGainController.ClassId, "SimpleGain Controller"));
        return factory;
    }

    [ModuleInitializer]
    internal static void ExportThisPlugin()
    {
        AudioPluginFactoryExporter.Instance = GetFactory();
    }
}
