using System.Runtime.CompilerServices;
using NPlug;
namespace SR.AnalogGain;

public static class AnalogGainPlugin
{
    public static AudioPluginFactory GetFactory()
    {
        var factory = new AudioPluginFactory(new("Sonic Riot", "https://sonicriotstudio.wordpress.com/", "sonicriotstudio@gmail.com"));
        factory.RegisterPlugin<AnalogGainProcessor>(new(AnalogGainProcessor.ClassId, "AnalogGain", AudioProcessorCategory.EffectDynamics));
        factory.RegisterPlugin<AnalogGainController>(new(AnalogGainController.ClassId, "AnalogGain Controller"));
        return factory;
    }

    [ModuleInitializer]
    internal static void ExportThisPlugin()
    {
        AudioPluginFactoryExporter.Instance = GetFactory();
    }
}
