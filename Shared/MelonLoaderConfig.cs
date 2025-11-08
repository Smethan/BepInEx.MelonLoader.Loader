using BepInEx.Configuration;

namespace BepInEx.MelonLoader.Loader.Shared;

public class MelonLoaderConfig
{
    // Loader settings
    public ConfigEntry<bool> DisableMods { get; set; }
    public ConfigEntry<bool> DebugMode { get; set; }
    public ConfigEntry<bool> CapturePlayerLogs { get; set; }
    public ConfigEntry<string> HarmonyLogLevel { get; set; }
    public ConfigEntry<bool> ForceQuit { get; set; }
    public ConfigEntry<bool> DisableStartScreen { get; set; }
    public ConfigEntry<bool> LaunchDebugger { get; set; }
    public ConfigEntry<string> ConsoleTheme { get; set; }

    // Console settings
    public ConfigEntry<bool> HideWarnings { get; set; }
    public ConfigEntry<bool> HideConsole { get; set; }
    public ConfigEntry<bool> ConsoleOnTop { get; set; }
    public ConfigEntry<bool> DontSetTitle { get; set; }

    // Logs settings
    public ConfigEntry<int> MaxLogs { get; set; }

    // Mono Debug Server (for Unity Mono games only)
    public ConfigEntry<bool> DebugSuspend { get; set; }
    public ConfigEntry<string> DebugIPAddress { get; set; }
    public ConfigEntry<int> DebugPort { get; set; }

    // Unity Engine settings
    public ConfigEntry<string> UnityVersionOverride { get; set; }
    public ConfigEntry<bool> DisableConsoleLogCleaner { get; set; }
    public ConfigEntry<string> MonoSearchPathOverride { get; set; }
    public ConfigEntry<bool> ForceOfflineGeneration { get; set; }
    public ConfigEntry<string> ForceGeneratorRegex { get; set; }
    public ConfigEntry<string> ForceGeneratorVersion { get; set; }
    public ConfigEntry<bool> EnableAssemblyGeneration { get; set; }
}
