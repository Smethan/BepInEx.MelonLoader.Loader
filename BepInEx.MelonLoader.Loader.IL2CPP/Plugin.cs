using BepInEx.IL2CPP;
using BepInEx.MelonLoader.Loader.Shared;

namespace BepInEx.MelonLoader.Loader.IL2CPP;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    public override void Load()
    {
        BootstrapShim.EnsureInitialized();

        BootstrapShim.RunMelonLoader(message => Log.LogError(message));
    }
}
