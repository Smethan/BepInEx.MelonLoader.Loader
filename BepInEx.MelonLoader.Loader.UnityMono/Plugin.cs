using BepInEx.MelonLoader.Loader.Shared;

namespace BepInEx.MelonLoader.Loader.UnityMono;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private void Awake()
    {
        BootstrapShim.EnsureInitialized();

        if (!BootstrapShim.RunMelonLoader(Logger.LogError))
            return;
    }
}
