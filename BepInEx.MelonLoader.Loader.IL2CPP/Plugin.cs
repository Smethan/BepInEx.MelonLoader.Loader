using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using BepInEx.MelonLoader.Loader.Shared;

namespace BepInEx.MelonLoader.Loader.IL2CPP;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    public override void Load()
    {
        // Create BepInEx config entries
        var bepInExConfig = new MelonLoaderConfig
        {
            // Loader settings
            DisableMods = Config.Bind("Loader", "DisableMods", false,
                "Disables MelonLoader. Equivalent to the '--no-mods' launch option"),
            DebugMode = Config.Bind("Loader", "DebugMode", false,
                "Enables debug mode. Equivalent to the '--melonloader.debug' launch option"),
            CapturePlayerLogs = Config.Bind("Loader", "CapturePlayerLogs", false,
                "Capture all Unity player logs into MelonLoader's logs even if the game disabled them. Equivalent to the '--melonloader.captureplayerlogs' launch option"),
            HarmonyLogLevel = Config.Bind("Loader", "HarmonyLogLevel", "Warn",
                "The maximum Harmony log verbosity to capture. Possible values: \"None\", \"Error\", \"Warn\", \"Info\", \"Debug\", \"IL\". Equivalent to the '--melonloader.harmonyloglevel' launch option"),
            ForceQuit = Config.Bind("Loader", "ForceQuit", false,
                "Only use this if the game freezes when trying to quit. Equivalent to the '--quitfix' launch option"),
            DisableStartScreen = Config.Bind("Loader", "DisableStartScreen", false,
                "Disables the start screen. Equivalent to the '--melonloader.disablestartscreen' launch option"),
            LaunchDebugger = Config.Bind("Loader", "LaunchDebugger", false,
                "Starts the dotnet debugger and waits for it to attach (IL2CPP games only). Equivalent to the '--melonloader.launchdebugger' launch option"),
            ConsoleTheme = Config.Bind("Loader", "ConsoleTheme", "Normal",
                "Sets the loader theme. Available themes: \"Normal\", \"Lemon\". Equivalent to the '--melonloader.consolemode' launch option (0 for Normal, 4 for Lemon)"),

            // Console settings
            HideWarnings = Config.Bind("Console", "HideWarnings", false,
                "Hides warnings from displaying. Equivalent to the '--melonloader.hidewarnings' launch option"),
            HideConsole = Config.Bind("Console", "HideConsole", false,
                "Hides the console. Equivalent to the '--melonloader.hideconsole' launch option"),
            ConsoleOnTop = Config.Bind("Console", "ConsoleOnTop", false,
                "Forces the console to always stay on-top of all other applications. Equivalent to the '--melonloader.consoleontop' launch option"),
            DontSetTitle = Config.Bind("Console", "DontSetTitle", false,
                "Keeps the console title as original. Equivalent to the '--melonloader.consoledst' launch option"),

            // Logs settings
            MaxLogs = Config.Bind("Logs", "MaxLogs", 10,
                "Sets the maximum amount of log files in the Logs folder. Equivalent to the '--melonloader.maxlogs' launch option"),

            // Mono Debug Server (for Unity Mono games only)
            DebugSuspend = Config.Bind("MonoDebugServer", "DebugSuspend", false,
                "Wait until a debugger is attached when debug_mode is true (Mono games only). Equivalent to the '--melonloader.debugsuspend' launch option"),
            DebugIPAddress = Config.Bind("MonoDebugServer", "DebugIPAddress", "127.0.0.1",
                "The IP address the Mono debug server will listen to (Mono games only). Equivalent to the '--melonloader.debugipaddress' launch option"),
            DebugPort = Config.Bind("MonoDebugServer", "DebugPort", 55555,
                "The port the Mono debug server will listen to (Mono games only). Equivalent to the '--melonloader.debugport' launch option"),

            // Unity Engine settings
            UnityVersionOverride = Config.Bind("UnityEngine", "UnityVersionOverride", "",
                "Overrides the detected UnityEngine version. Equivalent to the '--melonloader.unityversion' launch option"),
            DisableConsoleLogCleaner = Config.Bind("UnityEngine", "DisableConsoleLogCleaner", false,
                "Disables the console log cleaner (IL2CPP games only). Equivalent to the '--melonloader.disableunityclc' launch option"),
            MonoSearchPathOverride = Config.Bind("UnityEngine", "MonoSearchPathOverride", "",
                "A semicolon (;) separated list of paths for Mono to prioritize when seeking core libraries. Equivalent to the '--melonloader.monosearchpathoverride' launch option"),
            ForceOfflineGeneration = Config.Bind("UnityEngine", "ForceOfflineGeneration", false,
                "Forces the Il2Cpp Assembly Generator to run without contacting the remote API. Equivalent to the '--melonloader.agfoffline' launch option"),
            ForceGeneratorRegex = Config.Bind("UnityEngine", "ForceGeneratorRegex", "",
                "Forces the Il2Cpp Assembly Generator to use the specified regex. Equivalent to the '--melonloader.agfregex' launch option"),
            ForceGeneratorVersion = Config.Bind("UnityEngine", "ForceGeneratorVersion", "",
                "Forces the Il2Cpp Assembly Generator to use a specific version. Equivalent to the '--melonloader.agfvgenerator' launch option"),
            EnableAssemblyGeneration = Config.Bind("UnityEngine", "EnableAssemblyGeneration", false,
                "If true, MelonLoader will generate its own set of unhollowed assemblies alongside BepInEx. See README for usage instructions.")
        };

        BootstrapShim.EnsureInitialized();
        BootstrapShim.SetBepInExConfig(bepInExConfig);

        // Defer MelonLoader initialization until all BepInEx plugins have loaded
        // This prevents MelonLoader's Il2CppInterop patches from breaking other BepInEx plugins' Harmony patches
        Log.LogInfo("Waiting for all BepInEx plugins to load before initializing MelonLoader...");

        IL2CPPChainloader.Instance.Finished += () =>
        {
            Log.LogInfo("===== ALL BEPINEX PLUGINS LOADED =====");
            Log.LogInfo("Initializing MelonLoader now...");
            BootstrapShim.RunMelonLoader(message => Log.LogError(message));
            Log.LogInfo("MelonLoader initialization complete.");

            // Notify InteropRedirector that assemblies are now available (via reflection to avoid circular dependency)
            try
            {
                Log.LogInfo("Notifying InteropRedirector that MelonLoader assemblies are ready...");
                var interopRedirectorType = System.Type.GetType("BepInEx.MelonLoader.InteropRedirector.InteropRedirectorPatcher, BepInEx.MelonLoader.InteropRedirector");
                if (interopRedirectorType != null)
                {
                    var notifyMethod = interopRedirectorType.GetMethod("NotifyMelonLoaderReady", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (notifyMethod != null)
                    {
                        notifyMethod.Invoke(null, null);
                        Log.LogInfo("InteropRedirector notification sent successfully");
                    }
                    else
                    {
                        Log.LogWarning("NotifyMelonLoaderReady method not found on InteropRedirectorPatcher");
                    }
                }
                else
                {
                    Log.LogDebug("InteropRedirectorPatcher type not found (may not be installed)");
                }
            }
            catch (System.Exception ex)
            {
                Log.LogWarning($"Failed to notify InteropRedirector: {ex.Message}");
            }
        };
    }
}
