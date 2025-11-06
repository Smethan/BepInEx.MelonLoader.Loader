using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Logging;
using MelonLoader;
using MelonLoader.Utils;
using MonoMod.RuntimeDetour;
using ColorARGB = MelonLoader.Bootstrap.Logging.ColorARGB;
using Tomlet;

namespace BepInEx.MelonLoader.Loader.Shared;

internal static class BootstrapShim
{
    private static readonly object InitLock = new();
    private static bool _isInitialized;
    private static readonly ManualLogSource Log = Logger.CreateLogSource("MelonLoaderBootstrap");

    private static readonly Dictionary<nint, DetourState> Detours = new();

    private sealed class DetourState
    {
        public NativeDetour Detour { get; set; }
        public nint OriginalTarget { get; set; }
    }

    private static nint _monoRuntimeHandle;
    private static MonoGetRootDomainDelegate _monoGetRootDomain;
    private static MonoDomainGetDelegate _monoDomainGet;
    private static ResolveEventHandler _monoResolveHandler;
    private static Func<string, Assembly> _monoSearchDirectoryScan;

    internal static void EnsureInitialized()
    {
        if (_isInitialized)
            return;

        lock (InitLock)
        {
            if (_isInitialized)
                return;

            EnsureDirectoryLayout();

            var melonAssembly = typeof(MelonLoader.MelonLaunchOptions).Assembly;
            var bootstrapLibraryType = melonAssembly.GetType("MelonLoader.InternalUtils.BootstrapLibrary", throwOnError: true);
            var library = Activator.CreateInstance(bootstrapLibraryType, nonPublic: true);

            BindDelegate(library, "NativeHookAttach", nameof(NativeHookAttach));
            BindDelegate(library, "NativeHookDetach", nameof(NativeHookDetach));
            BindDelegate(library, "LogMsg", nameof(LogMsg));
            BindDelegate(library, "LogError", nameof(LogError));
            BindDelegate(library, "LogMelonInfo", nameof(LogMelonInfo));
            BindDelegate(library, "MonoInstallHooks", nameof(MonoInstallHooks));
            BindDelegate(library, "MonoGetDomainPtr", nameof(MonoGetDomainPtr));
            BindDelegate(library, "MonoGetRuntimeHandle", nameof(MonoGetRuntimeHandle));
            BindDelegate(library, "IsConsoleOpen", nameof(IsConsoleOpen));
            BindDelegate(library, "GetLoaderConfig", nameof(GetLoaderConfig));

            var bootstrapInteropType = melonAssembly.GetType("MelonLoader.InternalUtils.BootstrapInterop", throwOnError: true);
            var libraryProperty = bootstrapInteropType.GetProperty("Library", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            libraryProperty?.SetValue(null, library);

            _isInitialized = true;
        }
    }

    private static void BindDelegate(object target, string propertyName, string methodName)
    {
        if (target == null)
            return;

        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null)
        {
            Log.LogDebug($"Failed to bind delegate '{propertyName}' (property not found).");
            return;
        }

        var method = typeof(BootstrapShim).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        if (method == null)
        {
            Log.LogDebug($"Failed to bind delegate '{propertyName}' (method {methodName} not found).");
            return;
        }

        try
        {
            var delegateInstance = Delegate.CreateDelegate(property.PropertyType, method);
            property.SetValue(target, delegateInstance);
        }
        catch (Exception ex)
        {
            Log.LogDebug($"Failed to bind delegate '{propertyName}': {ex.Message}");
        }
    }

    internal static bool RunMelonLoader(Action<string> errorLogger)
    {
        var melonAssembly = typeof(MelonLoader.MelonLaunchOptions).Assembly;
        var coreType = melonAssembly.GetType("MelonLoader.Core", throwOnError: true);
        var initializeMethod = coreType.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        var startMethod = coreType.GetMethod("Start", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

        if (initializeMethod == null || startMethod == null)
        {
            errorLogger?.Invoke("MelonLoader core entry points not found.");
            return false;
        }

        var initResult = initializeMethod.Invoke(null, null);
        if (initResult is int initCode && initCode != 0)
        {
            errorLogger?.Invoke($"MelonLoader initialization failed with code {initCode}.");
            return false;
        }

        var startResult = startMethod.Invoke(null, null);
        if (startResult is bool started && started)
            return true;

        errorLogger?.Invoke("MelonLoader failed to start.");
        return false;
    }

    private static string GetBaseDirectory()
    {
        var configuredBaseDir = ArgParser.GetValue("melonloader.basedir");
        string baseRoot;

        if (!string.IsNullOrWhiteSpace(configuredBaseDir) && Directory.Exists(configuredBaseDir))
        {
            baseRoot = Path.GetFullPath(configuredBaseDir);
        }
        else
        {
            baseRoot = Paths.GameRootPath ?? AppContext.BaseDirectory;
        }

        return Path.Combine(baseRoot, "MLLoader");
    }

    private static void EnsureDirectoryLayout()
    {
        var baseDir = GetBaseDirectory();

        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(Path.Combine(baseDir, "Mods"));
        Directory.CreateDirectory(Path.Combine(baseDir, "Plugins"));
        Directory.CreateDirectory(Path.Combine(baseDir, "UserData"));
        Directory.CreateDirectory(Path.Combine(baseDir, "UserLibs"));
        Directory.CreateDirectory(Path.Combine(baseDir, "MelonLoader"));
        Directory.CreateDirectory(Path.Combine(baseDir, "MelonLoader", "Dependencies"));
        Directory.CreateDirectory(Path.Combine(baseDir, "MelonLoader", "Il2CppAssemblies"));
    }

    private static unsafe void NativeHookAttach(nint* target, nint detour)
    {
        if (target == null || *target == 0 || detour == 0)
            throw new ArgumentException("Invalid native hook arguments.");

        var originalPtr = *target;
        var detourPtr = detour;

        var detourInstance = new MonoMod.RuntimeDetour.NativeDetour(
            (IntPtr)originalPtr,
            (IntPtr)detourPtr);

        detourInstance.Apply();

        var trampolinePtr = detourInstance.GenerateTrampoline();

        lock (Detours)
        {
            Detours[(nint)trampolinePtr] = new DetourState
            {
                Detour = detourInstance,
                OriginalTarget = originalPtr
            };
        }

        *target = (nint)trampolinePtr;
    }

    private static unsafe void NativeHookDetach(nint* target, nint detour)
    {
        if (target == null || *target == 0)
            return;

        var trampoline = *target;

        lock (Detours)
        {
            if (!Detours.TryGetValue(trampoline, out var state))
                return;

            try
            {
                state.Detour.Dispose();
            }
            finally
            {
                Detours.Remove(trampoline);
                *target = state.OriginalTarget;
            }
        }
    }

    private static unsafe void LogMsg(ColorARGB* msgColor, string msg, int msgLength, ColorARGB* sectionColor, string section, int sectionLength)
    {
        var text = SliceString(msg, msgLength);
        var sectionText = SliceString(section, sectionLength);

        if (!string.IsNullOrEmpty(sectionText))
            Log.LogInfo($"[{sectionText}] {text}");
        else if (!string.IsNullOrEmpty(text))
            Log.LogInfo(text);
        else
            Log.LogMessage(string.Empty);
    }

    private static void LogError(string msg, int msgLength, string section, int sectionLength, bool warning)
    {
        var text = SliceString(msg, msgLength);
        var sectionText = SliceString(section, sectionLength);

        if (warning)
            Log.LogWarning(FormatSection(sectionText, text));
        else
            Log.LogError(FormatSection(sectionText, text));
    }

    private static unsafe void LogMelonInfo(ColorARGB* nameColor, string name, int nameLength, string info, int infoLength)
    {
        var nameText = SliceString(name, nameLength);
        var infoText = SliceString(info, infoLength);
        Log.LogInfo($"[{nameText}] {infoText}");
    }

    private static string SliceString(string value, int length)
    {
        if (string.IsNullOrEmpty(value) || length <= 0)
            return string.Empty;

        if (length >= value.Length)
            return value;

        return value.Substring(0, length);
    }

    private static string FormatSection(string section, string message)
    {
        if (string.IsNullOrEmpty(section))
            return message;
        return $"[{section}] {message}";
    }

    private static void MonoInstallHooks()
    {
        if (_monoResolveHandler != null)
            return;

        _monoSearchDirectoryScan ??= CreateMonoSearchDirectoryDelegate();

        _monoResolveHandler = (sender, args) =>
        {
            try
            {
                var requestedName = new AssemblyName(args.Name).Name;
                if (string.IsNullOrEmpty(requestedName))
                    return null;

                // Prefer MelonLoader's own search directory logic when available.
                var assembly = _monoSearchDirectoryScan?.Invoke(requestedName);
                if (assembly != null)
                    return assembly;

                return ResolveFromKnownDirectories(requestedName);
            }
            catch (Exception ex)
            {
                Log.LogDebug($"Mono assembly resolve failed: {ex.Message}");
                return null;
            }
        };

        AppDomain.CurrentDomain.AssemblyResolve += _monoResolveHandler;
    }

    private static nint MonoGetDomainPtr()
    {
        var handle = EnsureMonoRuntimeHandle();
        if (handle == nint.Zero)
            return nint.Zero;

        EnsureMonoDelegates(handle);

        var domain = _monoGetRootDomain != null ? _monoGetRootDomain() : nint.Zero;
        if (domain == nint.Zero && _monoDomainGet != null)
            domain = _monoDomainGet();

        return domain;
    }

    private static nint MonoGetRuntimeHandle()
    {
        return EnsureMonoRuntimeHandle();
    }

    private static nint EnsureMonoRuntimeHandle()
    {
        if (_monoRuntimeHandle != nint.Zero)
            return _monoRuntimeHandle;

        foreach (var candidate in EnumerateMonoLibraryCandidates())
        {
            var handle = LoadMonoLibrary(candidate);
            if (handle != nint.Zero)
            {
                _monoRuntimeHandle = handle;
                break;
            }
        }

        if (_monoRuntimeHandle == nint.Zero)
            Log.LogWarning("Failed to locate the Mono runtime library. Mono-based titles may not function correctly.");

        return _monoRuntimeHandle;
    }

    private static void EnsureMonoDelegates(nint handle)
    {
        if (_monoGetRootDomain == null)
        {
            var export = GetExportSafe(handle, "mono_get_root_domain");
            if (export != nint.Zero)
                _monoGetRootDomain = (MonoGetRootDomainDelegate)Marshal.GetDelegateForFunctionPointer(export, typeof(MonoGetRootDomainDelegate));
        }

        if (_monoDomainGet == null)
        {
            var export = GetExportSafe(handle, "mono_domain_get");
            if (export != nint.Zero)
                _monoDomainGet = (MonoDomainGetDelegate)Marshal.GetDelegateForFunctionPointer(export, typeof(MonoDomainGetDelegate));
        }
    }

    private static IEnumerable<string> EnumerateMonoLibraryCandidates()
    {
        string[] names;
        var platform = Environment.OSVersion.Platform;

        if (platform == PlatformID.Win32NT)
        {
            names = new[] { "mono-2.0-bdwgc.dll", "mono-2.0-sgen.dll", "mono.dll" };
        }
        else if (platform == PlatformID.MacOSX)
        {
            names = new[] { "libmonobdwgc-2.0.dylib", "libmono-2.0.dylib", "libmono.0.dylib" };
        }
        else
        {
            names = new[] { "libmonobdwgc-2.0.so", "libmono-2.0.so", "libmono.so" };
        }

        foreach (var name in names)
            yield return name;

        var gameRoot = Paths.GameRootPath;
        if (string.IsNullOrEmpty(gameRoot))
            yield break;

        var dataDirectory = Path.Combine(gameRoot, $"{Paths.ProcessName}_Data");
        var candidates = new[]
        {
            Path.Combine(dataDirectory, "MonoBleedingEdge", "EmbedRuntime"),
            Path.Combine(dataDirectory, "MonoBleedingEdge"),
            Path.Combine(dataDirectory, "Mono")
        };

        foreach (var directory in candidates)
        {
            foreach (var name in names)
                yield return Path.Combine(directory, name);
        }
    }

    private static nint LoadMonoLibrary(string path)
    {
        try
        {
            return MelonLoader.NativeLibrary.AgnosticLoadLibrary(path);
        }
        catch
        {
            return nint.Zero;
        }
    }

    private static nint GetExportSafe(nint handle, string name)
    {
        if (handle == nint.Zero)
            return nint.Zero;

        try
        {
            return MelonLoader.NativeLibrary.GetExport(handle, name);
        }
        catch
        {
            return nint.Zero;
        }
    }

    [return: MarshalAs(UnmanagedType.U1)]
    private static bool IsConsoleOpen() => true;

    private static void GetLoaderConfig(ref LoaderConfig config)
    {
        var preparedConfig = PrepareLoaderConfig();
        config = preparedConfig;
        LoaderConfig.Current = preparedConfig;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr MonoGetRootDomainDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr MonoDomainGetDelegate();

    private static LoaderConfig PrepareLoaderConfig()
    {
        var baseDir = GetBaseDirectory();
        var configPath = Path.Combine(baseDir, "UserData", "Loader.cfg");
        LoaderConfig config = new();

        if (File.Exists(configPath))
        {
            try
            {
                var document = TomlParser.ParseFile(configPath);
                config = TomletMain.To<LoaderConfig>(document) ?? config;
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Failed to parse Loader.cfg: {ex.Message}");
            }
        }

        config.Loader.BaseDirectory = baseDir;
        ApplyLaunchOverrides(config);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            var output = TomletMain.TomlStringFrom(config);
            File.WriteAllText(configPath, output);
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Failed to write Loader.cfg: {ex.Message}");
        }

        return config;
    }

    private static Func<string, Assembly> CreateMonoSearchDirectoryDelegate()
    {
        try
        {
            var searchManagerType = typeof(MelonLoader.Core).Assembly.GetType("MelonLoader.MonoInternals.ResolveInternals.SearchDirectoryManager");
            var scanMethod = searchManagerType?.GetMethod("Scan", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (scanMethod == null)
                return null;

            return requestedName => scanMethod.Invoke(null, new object[] { requestedName }) as Assembly;
        }
        catch (Exception ex)
        {
            Log.LogDebug($"Failed to bind SearchDirectoryManager.Scan: {ex.Message}");
            return null;
        }
    }

    private static Assembly ResolveFromKnownDirectories(string assemblyName)
    {
        var searchPaths = new List<string>
        {
            MelonLoader.Utils.MelonEnvironment.MelonLoaderDirectory,
            Path.Combine(MelonLoader.Utils.MelonEnvironment.MelonLoaderDirectory, "net35"),
            Path.Combine(MelonLoader.Utils.MelonEnvironment.MelonLoaderDirectory, "net6"),
            MelonLoader.Utils.MelonEnvironment.PluginsDirectory,
            MelonLoader.Utils.MelonEnvironment.ModsDirectory,
            MelonLoader.Utils.MelonEnvironment.UserLibsDirectory
        };

        foreach (var directory in searchPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
        {
            if (!Directory.Exists(directory))
                continue;

            var candidate = Path.Combine(directory, assemblyName + ".dll");
            if (!File.Exists(candidate))
                continue;

            try
            {
                return Assembly.LoadFrom(candidate);
            }
            catch (Exception ex)
            {
                Log.LogDebug($"Failed to load {candidate}: {ex.Message}");
            }
        }

        return null;
    }

    private static void ApplyLaunchOverrides(LoaderConfig config)
    {
        if (ArgParser.IsDefined("no-mods"))
            config.Loader.Disable = true;

        if (ArgParser.IsDefined("melonloader.debug"))
            config.Loader.DebugMode = true;

        if (ArgParser.IsDefined("melonloader.captureplayerlogs"))
            config.Loader.CapturePlayerLogs = true;

        var harmonyLevel = ArgParser.GetValue("melonloader.harmonyloglevel");
        if (!string.IsNullOrWhiteSpace(harmonyLevel) &&
            Enum.TryParse(harmonyLevel, true, out LoaderConfig.CoreConfig.HarmonyLogVerbosity verbosity))
        {
            config.Loader.HarmonyLogLevel = verbosity;
        }

        if (ArgParser.IsDefined("quitfix"))
            config.Loader.ForceQuit = true;

        if (ArgParser.IsDefined("melonloader.disablestartscreen"))
            config.Loader.DisableStartScreen = true;

        if (ArgParser.IsDefined("melonloader.launchdebugger"))
            config.Loader.LaunchDebugger = true;

        var consoleMode = ArgParser.GetValue("melonloader.consolemode");
        config.Loader.Theme = LoaderConfig.CoreConfig.LoaderTheme.Normal;
        if (int.TryParse(consoleMode, out var modeValue))
        {
            var min = (int)LoaderConfig.CoreConfig.LoaderTheme.Normal;
            var max = (int)LoaderConfig.CoreConfig.LoaderTheme.Lemon;
            if (modeValue < min)
                modeValue = min;
            if (modeValue > max)
                modeValue = max;

            config.Loader.Theme = (LoaderConfig.CoreConfig.LoaderTheme)modeValue;
        }

        if (ArgParser.IsDefined("melonloader.hideconsole"))
            config.Console.Hide = true;

        if (ArgParser.IsDefined("melonloader.consoleontop"))
            config.Console.AlwaysOnTop = true;

        if (ArgParser.IsDefined("melonloader.consoledst"))
            config.Console.DontSetTitle = true;

        if (ArgParser.IsDefined("melonloader.hidewarnings"))
            config.Console.HideWarnings = true;

        var maxLogsValue = ArgParser.GetValue("melonloader.maxlogs");
        if (uint.TryParse(maxLogsValue, out var maxLogs))
            config.Logs.MaxLogs = maxLogs;

        if (ArgParser.IsDefined("melonloader.debugsuspend"))
            config.MonoDebugServer.DebugSuspend = true;

        var debugIp = ArgParser.GetValue("melonloader.debugipaddress");
        if (!string.IsNullOrWhiteSpace(debugIp))
            config.MonoDebugServer.DebugIpAddress = debugIp;

        var debugPort = ArgParser.GetValue("melonloader.debugport");
        if (uint.TryParse(debugPort, out var portValue))
            config.MonoDebugServer.DebugPort = portValue;

        var unityVersionOverride = ArgParser.GetValue("melonloader.unityversion");
        if (!string.IsNullOrWhiteSpace(unityVersionOverride))
            config.UnityEngine.VersionOverride = unityVersionOverride;

        if (ArgParser.IsDefined("melonloader.disableunityclc"))
            config.UnityEngine.DisableConsoleLogCleaner = true;

        var monoSearchOverride = ArgParser.GetValue("melonloader.monosearchpathoverride");
        if (!string.IsNullOrWhiteSpace(monoSearchOverride))
            config.UnityEngine.MonoSearchPathOverride = monoSearchOverride;

        if (ArgParser.IsDefined("melonloader.agfoffline"))
            config.UnityEngine.ForceOfflineGeneration = true;

        var agfRegex = ArgParser.GetValue("melonloader.agfregex");
        if (!string.IsNullOrWhiteSpace(agfRegex))
            config.UnityEngine.ForceGeneratorRegex = agfRegex;

        var agfDumper = ArgParser.GetValue("melonloader.agfvdumper");
        if (!string.IsNullOrWhiteSpace(agfDumper))
            config.UnityEngine.ForceIl2CppDumperVersion = agfDumper;

        if (ArgParser.IsDefined("melonloader.agfregenerate"))
            config.UnityEngine.ForceRegeneration = true;

        if (ArgParser.IsDefined("cpp2il.callanalyzer"))
            config.UnityEngine.EnableCpp2ILCallAnalyzer = true;

        if (ArgParser.IsDefined("cpp2il.nativemethoddetector"))
            config.UnityEngine.EnableCpp2ILNativeMethodDetector = true;
    }
}
