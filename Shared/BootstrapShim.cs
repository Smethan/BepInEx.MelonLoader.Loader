using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Logging;
using MelonLoader;
using MelonLoader.Utils;
using MonoMod.RuntimeDetour;
using ColorARGB = global::MelonLoader.Logging.ColorARGB;
using Tomlet;
using LoaderConfig = global::MelonLoader.LoaderConfig;
using MelonLaunchOptions = global::MelonLoader.MelonLaunchOptions;
using MelonEnvironment = global::MelonLoader.Utils.MelonEnvironment;

namespace BepInEx.MelonLoader.Loader.Shared;

internal static class BootstrapShim
{
    private static readonly object InitLock = new();
    private static bool _isInitialized;
    private static readonly ManualLogSource Log = Logger.CreateLogSource("MelonLoaderBootstrap");
    private static MelonLoaderConfig _bepInExConfig;

    private static readonly Dictionary<IntPtr, DetourState> Detours = new();

    private sealed class DetourState
    {
        public NativeDetour Detour { get; set; }
        public IntPtr OriginalTarget { get; set; }
        public Delegate TrampolineDelegate { get; set; }  // Keep delegate alive to prevent GC
    }

    private static IntPtr _monoRuntimeHandle;
    private static MonoGetRootDomainDelegate _monoGetRootDomain;
    private static MonoDomainGetDelegate _monoDomainGet;
    private static ResolveEventHandler _monoResolveHandler;
    private static Func<string, Assembly> _monoSearchDirectoryScan;

    // Helper to check if string is null or whitespace (for .NET 3.5 compatibility)
    private static bool IsNullOrWhiteSpace(string value)
    {
        return string.IsNullOrEmpty(value) || value.Trim().Length == 0;
    }

    // Helper to set properties with internal setters via reflection
    private static void SetInternalProperty(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property != null && property.CanWrite)
        {
            property.SetValue(target, value, null);
        }
    }

    // Helper for Enum.TryParse (.NET 3.5 compatibility)
    private static bool TryParseEnum<T>(string value, bool ignoreCase, out T result) where T : struct
    {
        try
        {
            result = (T)Enum.Parse(typeof(T), value, ignoreCase);
            return true;
        }
        catch
        {
            result = default(T);
            return false;
        }
    }

    internal static void EnsureInitialized()
    {
        if (_isInitialized)
            return;

        lock (InitLock)
        {
            if (_isInitialized)
                return;

            EnsureDirectoryLayout();

            var melonAssembly = typeof(MelonLaunchOptions).Assembly;
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
            libraryProperty?.SetValue(null, library, null);

            _isInitialized = true;
        }
    }

    internal static void SetBepInExConfig(MelonLoaderConfig config)
    {
        _bepInExConfig = config;
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
            property.SetValue(target, delegateInstance, null);
        }
        catch (Exception ex)
        {
            Log.LogDebug($"Failed to bind delegate '{propertyName}': {ex.Message}");
        }
    }

    internal static bool RunMelonLoader(Action<string> errorLogger)
    {
        var melonAssembly = typeof(MelonLaunchOptions).Assembly;
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

        if (!IsNullOrWhiteSpace(configuredBaseDir) && Directory.Exists(configuredBaseDir))
        {
            return Path.GetFullPath(configuredBaseDir);
        }

        // Get the location of this plugin DLL
        var pluginLocation = typeof(BootstrapShim).Assembly.Location;
        var pluginDir = Path.GetDirectoryName(pluginLocation);

        // After r2modman install structure:
        // BepInEx/plugins/BepInEx.MelonLoader.Loader/Plugin.dll
        // MLLoader is at: BepInEx/plugins/MLLoader/
        // So go up one directory from plugin subfolder to BepInEx/plugins folder
        var pluginsDir = Path.GetDirectoryName(pluginDir);

        return Path.Combine(pluginsDir, "MLLoader");
    }

    private static string FindR2ModManProfile()
    {
        try
        {
            string r2modmanBase;

            // Determine r2modman config path based on OS
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                r2modmanBase = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "r2modmanPlus-local");
            }
            else
            {
                // Linux/Mac
                var configPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(configPath))
                    configPath = Path.Combine(Environment.GetEnvironmentVariable("HOME"), ".config");
                r2modmanBase = Path.Combine(configPath, "r2modmanPlus-local");
            }

            if (!Directory.Exists(r2modmanBase))
            {
                Log.LogDebug($"r2modman installation not found at {r2modmanBase}");
                return null;
            }

            // Detect game name from current process
            string gameName = Path.GetFileNameWithoutExtension(
                Process.GetCurrentProcess().MainModule.FileName);

            if (string.IsNullOrEmpty(gameName))
            {
                Log.LogDebug("Could not determine game executable name");
                return null;
            }

            // Check for game-specific profile directory
            var gameProfileBase = Path.Combine(r2modmanBase, gameName);
            string gameProfileDir = Path.Combine(gameProfileBase, "profiles");
            if (!Directory.Exists(gameProfileDir))
            {
                Log.LogDebug($"No r2modman profiles found for game '{gameName}' at {gameProfileDir}");
                return null;
            }

            // Find most recently modified profile (likely the active one)
            var profiles = Directory.GetDirectories(gameProfileDir);
            if (profiles.Length == 0)
            {
                Log.LogDebug($"No profiles found in {gameProfileDir}");
                return null;
            }

            var activeProfile = profiles
                .OrderByDescending(Directory.GetLastWriteTime)
                .First();

            Log.LogInfo($"Detected r2modman profile: {activeProfile}");
            return activeProfile;
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Failed to detect r2modman profile: {ex.Message}");
            return null;
        }
    }

    private static bool IsSymbolicLink(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return false;

            var dirInfo = new DirectoryInfo(path);
            return (dirInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

    private static bool TryCreateSymlink(string linkPath, string targetPath)
    {
        try
        {
            // Remove existing if it's already a symlink
            if (Directory.Exists(linkPath))
            {
                if (IsSymbolicLink(linkPath))
                {
                    Log.LogDebug($"Removing existing symlink at {linkPath}");
                    Directory.Delete(linkPath);
                }
                else
                {
                    Log.LogDebug($"Directory already exists and is not a symlink: {linkPath}");
                    return false; // Don't overwrite real directories
                }
            }

            if (!Directory.Exists(targetPath))
            {
                Log.LogDebug($"Target directory does not exist: {targetPath}");
                return false;
            }

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                // Windows: Try to create directory symlink
                // dwFlags = 1 for directory symlink
                bool success = CreateSymbolicLink(linkPath, targetPath, 1);
                if (!success)
                {
                    Log.LogWarning($"Failed to create Windows symlink from {linkPath} to {targetPath}");
                    return false;
                }
            }
            else
            {
                // Linux/Mac: Use ln -s command
                var psi = new ProcessStartInfo
                {
                    FileName = "ln",
                    Arguments = $"-s \"{targetPath}\" \"{linkPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        var error = process.StandardError.ReadToEnd();
                        Log.LogWarning($"Failed to create symlink: {error}");
                        return false;
                    }
                }
            }

            Log.LogInfo($"Created symlink: {linkPath} -> {targetPath}");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Failed to create symlink from {linkPath} to {targetPath}: {ex.Message}");
            return false;
        }
    }

    private static void SetupR2ModManIntegration(string baseDir, string profilePath)
    {
        if (string.IsNullOrEmpty(profilePath))
            return;

        Log.LogInfo("Setting up r2modman integration...");

        // Try to create symlinks for Mods, Plugins, and UserData directories
        var symlinkPairs = new[]
        {
            new KeyValuePair<string, string>(Path.Combine(baseDir, "Mods"), Path.Combine(profilePath, "Mods")),
            new KeyValuePair<string, string>(Path.Combine(baseDir, "Plugins"), Path.Combine(profilePath, "Plugins")),
            new KeyValuePair<string, string>(Path.Combine(baseDir, "UserData"), Path.Combine(profilePath, "UserData"))
        };

        bool anySymlinkCreated = false;
        foreach (var pair in symlinkPairs)
        {
            var linkPath = pair.Key;
            var targetPath = pair.Value;
            if (TryCreateSymlink(linkPath, targetPath))
            {
                anySymlinkCreated = true;
            }
        }

        if (anySymlinkCreated)
        {
            Log.LogInfo("r2modman integration setup complete via symlinks");
        }
        else
        {
            Log.LogWarning("Could not create symlinks for r2modman integration");
            Log.LogInfo("MelonLoader mods from r2modman may not be discovered automatically");
            Log.LogInfo("Consider using --melonloader.basedir launch argument to point to r2modman profile");
        }
    }

    private static void EnsureDirectoryLayout()
    {
        var baseDir = GetBaseDirectory();
        Directory.CreateDirectory(baseDir);

        // Try to detect and setup r2modman integration
        var r2modmanProfile = FindR2ModManProfile();
        if (r2modmanProfile != null)
        {
            SetupR2ModManIntegration(baseDir, r2modmanProfile);
        }

        // Create directories that weren't symlinked (or all if no r2modman)
        var dirsToCreate = new[]
        {
            "Mods",
            "Plugins",
            "UserData",
            "UserLibs"
        };

        foreach (var dir in dirsToCreate)
        {
            var fullPath = Path.Combine(baseDir, dir);
            // Only create if it doesn't already exist (symlink or otherwise)
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
        }

        // Always create these MelonLoader-specific directories
        Directory.CreateDirectory(Path.Combine(baseDir, "MelonLoader"));
        Directory.CreateDirectory(Path.Combine(Path.Combine(baseDir, "MelonLoader"), "Dependencies"));
        Directory.CreateDirectory(Path.Combine(Path.Combine(baseDir, "MelonLoader"), "Il2CppAssemblies"));
    }

    // Dummy method used as a signature for NativeDetour
    // This must be a real static method (not a lambda) to avoid DynamicMethod.MethodHandle errors
    private static void DummyNativeSignature() { }

    private static unsafe void NativeHookAttach(IntPtr* target, IntPtr detour)
    {
        if (target == null || *target == IntPtr.Zero || detour == IntPtr.Zero)
            throw new ArgumentException("Invalid native hook arguments.");

        var originalPtr = *target;

        // Get the signature method (must be a real method, not a DynamicMethod/lambda)
        var signatureMethod = typeof(BootstrapShim).GetMethod(
            nameof(DummyNativeSignature),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Create NativeDetour with the signature method
        var detourInstance = new MonoMod.RuntimeDetour.NativeDetour(
            signatureMethod,  // Provides the signature for trampoline generation
            originalPtr,      // Original function address
            detour);          // Detour function address

        detourInstance.Apply();

        // GenerateTrampoline returns a MethodBase, but it might be a DynamicMethod
        // DynamicMethods don't have MethodHandles, so we need to handle them differently
        var trampolineMethod = detourInstance.GenerateTrampoline();

        IntPtr trampolinePtr;
        if (trampolineMethod is System.Reflection.Emit.DynamicMethod dynMethod)
        {
            // For DynamicMethod, create a delegate and get its function pointer
            var trampolineDelegate = dynMethod.CreateDelegate(typeof(Action));
            trampolinePtr = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(trampolineDelegate);

            // Keep the delegate alive to prevent GC
            lock (Detours)
            {
                Detours[trampolinePtr] = new DetourState
                {
                    Detour = detourInstance,
                    OriginalTarget = originalPtr,
                    TrampolineDelegate = trampolineDelegate  // Keep delegate alive
                };
            }
        }
        else
        {
            // For regular methods, use MethodHandle
            trampolinePtr = trampolineMethod.MethodHandle.GetFunctionPointer();

            lock (Detours)
            {
                Detours[trampolinePtr] = new DetourState
                {
                    Detour = detourInstance,
                    OriginalTarget = originalPtr
                };
            }
        }

        *target = trampolinePtr;
    }

    private static unsafe void NativeHookDetach(IntPtr* target, IntPtr detour)
    {
        if (target == null || *target == IntPtr.Zero)
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

    private static IntPtr MonoGetDomainPtr()
    {
        var handle = EnsureMonoRuntimeHandle();
        if (handle == IntPtr.Zero)
            return IntPtr.Zero;

        EnsureMonoDelegates(handle);

        var domain = _monoGetRootDomain != null ? _monoGetRootDomain() : IntPtr.Zero;
        if (domain == IntPtr.Zero && _monoDomainGet != null)
            domain = _monoDomainGet();

        return domain;
    }

    private static IntPtr MonoGetRuntimeHandle()
    {
        return EnsureMonoRuntimeHandle();
    }

    private static IntPtr EnsureMonoRuntimeHandle()
    {
        if (_monoRuntimeHandle != IntPtr.Zero)
            return _monoRuntimeHandle;

        foreach (var candidate in EnumerateMonoLibraryCandidates())
        {
            var handle = LoadMonoLibrary(candidate);
            if (handle != IntPtr.Zero)
            {
                _monoRuntimeHandle = handle;
                break;
            }
        }

        if (_monoRuntimeHandle == IntPtr.Zero)
            Log.LogWarning("Failed to locate the Mono runtime library. Mono-based titles may not function correctly.");

        return _monoRuntimeHandle;
    }

    private static void EnsureMonoDelegates(IntPtr handle)
    {
        if (_monoGetRootDomain == null)
        {
            var export = GetExportSafe(handle, "mono_get_root_domain");
            if (export != IntPtr.Zero)
                _monoGetRootDomain = (MonoGetRootDomainDelegate)Marshal.GetDelegateForFunctionPointer(export, typeof(MonoGetRootDomainDelegate));
        }

        if (_monoDomainGet == null)
        {
            var export = GetExportSafe(handle, "mono_domain_get");
            if (export != IntPtr.Zero)
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
            Path.Combine(Path.Combine(dataDirectory, "MonoBleedingEdge"), "EmbedRuntime"),
            Path.Combine(dataDirectory, "MonoBleedingEdge"),
            Path.Combine(dataDirectory, "Mono")
        };

        foreach (var directory in candidates)
        {
            foreach (var name in names)
                yield return Path.Combine(directory, name);
        }
    }

    private static IntPtr LoadMonoLibrary(string path)
    {
        try
        {
            // Use reflection to call MelonLoader.NativeLibrary.AgnosticLoadLibrary
            var melonAssembly = typeof(MelonLaunchOptions).Assembly;
            var nativeLibraryType = melonAssembly.GetType("MelonLoader.NativeLibrary");
            var loadMethod = nativeLibraryType?.GetMethod("AgnosticLoadLibrary", BindingFlags.Static | BindingFlags.Public);
            if (loadMethod != null)
            {
                var result = loadMethod.Invoke(null, new object[] { path });
                return result != null ? (IntPtr)result : IntPtr.Zero;
            }
            return IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static IntPtr GetExportSafe(IntPtr handle, string name)
    {
        if (handle == IntPtr.Zero)
            return IntPtr.Zero;

        try
        {
            // Use reflection to call MelonLoader.NativeLibrary.GetExport
            var melonAssembly = typeof(MelonLaunchOptions).Assembly;
            var nativeLibraryType = melonAssembly.GetType("MelonLoader.NativeLibrary");
            var getExportMethod = nativeLibraryType?.GetMethod("GetExport", BindingFlags.Static | BindingFlags.Public);
            if (getExportMethod != null)
            {
                var result = getExportMethod.Invoke(null, new object[] { handle, name });
                return result != null ? (IntPtr)result : IntPtr.Zero;
            }
            return IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    [return: MarshalAs(UnmanagedType.U1)]
    private static bool IsConsoleOpen() => true;

    private static void GetLoaderConfig(ref LoaderConfig config)
    {
        var preparedConfig = PrepareLoaderConfig();
        config = preparedConfig;

        // Set LoaderConfig.Current via reflection (internal setter)
        var currentProperty = typeof(LoaderConfig).GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
        if (currentProperty != null && currentProperty.CanWrite)
        {
            currentProperty.SetValue(null, preparedConfig, null);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr MonoGetRootDomainDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr MonoDomainGetDelegate();

    private static LoaderConfig PrepareLoaderConfig()
    {
        var baseDir = GetBaseDirectory();
        LoaderConfig config = new();

        // Apply BepInEx config values if available
        if (_bepInExConfig != null)
        {
            // Loader settings
            SetInternalProperty(config.Loader, "Disable", _bepInExConfig.DisableMods.Value);
            SetInternalProperty(config.Loader, "DebugMode", _bepInExConfig.DebugMode.Value);
            SetInternalProperty(config.Loader, "CapturePlayerLogs", _bepInExConfig.CapturePlayerLogs.Value);
            SetInternalProperty(config.Loader, "ForceQuit", _bepInExConfig.ForceQuit.Value);
            SetInternalProperty(config.Loader, "DisableStartScreen", _bepInExConfig.DisableStartScreen.Value);
            SetInternalProperty(config.Loader, "LaunchDebugger", _bepInExConfig.LaunchDebugger.Value);

            // Parse HarmonyLogLevel enum
            if (TryParseEnum<LoaderConfig.CoreConfig.HarmonyLogVerbosity>(_bepInExConfig.HarmonyLogLevel.Value, true, out var harmonyLevel))
            {
                SetInternalProperty(config.Loader, "HarmonyLogLevel", harmonyLevel);
            }

            // Parse ConsoleTheme enum
            if (TryParseEnum<LoaderConfig.CoreConfig.LoaderTheme>(_bepInExConfig.ConsoleTheme.Value, true, out var theme))
            {
                SetInternalProperty(config.Loader, "Theme", theme);
            }

            // Console settings
            SetInternalProperty(config.Console, "HideWarnings", _bepInExConfig.HideWarnings.Value);
            SetInternalProperty(config.Console, "HideConsole", _bepInExConfig.HideConsole.Value);
            SetInternalProperty(config.Console, "ConsoleOnTop", _bepInExConfig.ConsoleOnTop.Value);
            SetInternalProperty(config.Console, "DontSetTitle", _bepInExConfig.DontSetTitle.Value);

            // Logs settings
            SetInternalProperty(config.Logs, "MaxLogs", _bepInExConfig.MaxLogs.Value);

            // Mono Debug Server settings
            SetInternalProperty(config.MonoDebugServer, "DebugSuspend", _bepInExConfig.DebugSuspend.Value);
            SetInternalProperty(config.MonoDebugServer, "DebugIPAddress", _bepInExConfig.DebugIPAddress.Value);
            SetInternalProperty(config.MonoDebugServer, "DebugPort", _bepInExConfig.DebugPort.Value);

            // Unity Engine settings
            if (!string.IsNullOrEmpty(_bepInExConfig.UnityVersionOverride.Value))
            {
                SetInternalProperty(config.UnityEngine, "VersionOverride", _bepInExConfig.UnityVersionOverride.Value);
            }
            SetInternalProperty(config.UnityEngine, "DisableConsoleLogCleaner", _bepInExConfig.DisableConsoleLogCleaner.Value);
            if (!string.IsNullOrEmpty(_bepInExConfig.MonoSearchPathOverride.Value))
            {
                SetInternalProperty(config.UnityEngine, "MonoSearchPathOverride", _bepInExConfig.MonoSearchPathOverride.Value);
            }
            SetInternalProperty(config.UnityEngine, "ForceOfflineGeneration", _bepInExConfig.ForceOfflineGeneration.Value);
            if (!string.IsNullOrEmpty(_bepInExConfig.ForceGeneratorRegex.Value))
            {
                SetInternalProperty(config.UnityEngine, "ForceGeneratorRegex", _bepInExConfig.ForceGeneratorRegex.Value);
            }
            if (!string.IsNullOrEmpty(_bepInExConfig.ForceGeneratorVersion.Value))
            {
                SetInternalProperty(config.UnityEngine, "ForceGeneratorVersion", _bepInExConfig.ForceGeneratorVersion.Value);
            }
            SetInternalProperty(config.UnityEngine, "EnableAssemblyGeneration", _bepInExConfig.EnableAssemblyGeneration.Value);
        }

        SetInternalProperty(config.Loader, "BaseDirectory", baseDir);
        ApplyLaunchOverrides(config);

        return config;
    }

    private static Func<string, Assembly> CreateMonoSearchDirectoryDelegate()
    {
        try
        {
            var melonAssembly = typeof(MelonLaunchOptions).Assembly;
            var searchManagerType = melonAssembly.GetType("MelonLoader.MonoInternals.ResolveInternals.SearchDirectoryManager");
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
            MelonEnvironment.MelonLoaderDirectory,
            Path.Combine(MelonEnvironment.MelonLoaderDirectory, "net35"),
            Path.Combine(MelonEnvironment.MelonLoaderDirectory, "net6"),
            MelonEnvironment.PluginsDirectory,
            MelonEnvironment.ModsDirectory,
            MelonEnvironment.UserLibsDirectory
        };

        foreach (var directory in searchPaths.Where(p => !IsNullOrWhiteSpace(p)).Distinct())
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
            SetInternalProperty(config.Loader, "Disable", true);

        if (ArgParser.IsDefined("melonloader.debug"))
            SetInternalProperty(config.Loader, "DebugMode", true);

        if (ArgParser.IsDefined("melonloader.captureplayerlogs"))
            SetInternalProperty(config.Loader, "CapturePlayerLogs", true);

        var harmonyLevel = ArgParser.GetValue("melonloader.harmonyloglevel");
        if (!IsNullOrWhiteSpace(harmonyLevel) &&
            TryParseEnum(harmonyLevel, true, out LoaderConfig.CoreConfig.HarmonyLogVerbosity verbosity))
        {
            SetInternalProperty(config.Loader, "HarmonyLogLevel", verbosity);
        }

        if (ArgParser.IsDefined("quitfix"))
            SetInternalProperty(config.Loader, "ForceQuit", true);

        if (ArgParser.IsDefined("melonloader.disablestartscreen"))
            SetInternalProperty(config.Loader, "DisableStartScreen", true);

        if (ArgParser.IsDefined("melonloader.launchdebugger"))
            SetInternalProperty(config.Loader, "LaunchDebugger", true);

        var consoleMode = ArgParser.GetValue("melonloader.consolemode");
        if (int.TryParse(consoleMode, out var modeValue))
        {
            var min = (int)LoaderConfig.CoreConfig.LoaderTheme.Normal;
            var max = (int)LoaderConfig.CoreConfig.LoaderTheme.Lemon;
            if (modeValue < min)
                modeValue = min;
            if (modeValue > max)
                modeValue = max;

            SetInternalProperty(config.Loader, "Theme", (LoaderConfig.CoreConfig.LoaderTheme)modeValue);
        }

        if (ArgParser.IsDefined("melonloader.hideconsole"))
            SetInternalProperty(config.Console, "Hide", true);

        if (ArgParser.IsDefined("melonloader.consoleontop"))
            SetInternalProperty(config.Console, "AlwaysOnTop", true);

        if (ArgParser.IsDefined("melonloader.consoledst"))
            SetInternalProperty(config.Console, "DontSetTitle", true);

        if (ArgParser.IsDefined("melonloader.hidewarnings"))
            SetInternalProperty(config.Console, "HideWarnings", true);

        var maxLogsValue = ArgParser.GetValue("melonloader.maxlogs");
        if (uint.TryParse(maxLogsValue, out var maxLogs))
            SetInternalProperty(config.Logs, "MaxLogs", maxLogs);

        if (ArgParser.IsDefined("melonloader.debugsuspend"))
            SetInternalProperty(config.MonoDebugServer, "DebugSuspend", true);

        var debugIp = ArgParser.GetValue("melonloader.debugipaddress");
        if (!IsNullOrWhiteSpace(debugIp))
            SetInternalProperty(config.MonoDebugServer, "DebugIpAddress", debugIp);

        var debugPort = ArgParser.GetValue("melonloader.debugport");
        if (uint.TryParse(debugPort, out var portValue))
            SetInternalProperty(config.MonoDebugServer, "DebugPort", portValue);

        var unityVersionOverride = ArgParser.GetValue("melonloader.unityversion");
        if (!IsNullOrWhiteSpace(unityVersionOverride))
            SetInternalProperty(config.UnityEngine, "VersionOverride", unityVersionOverride);

        if (ArgParser.IsDefined("melonloader.disableunityclc"))
            SetInternalProperty(config.UnityEngine, "DisableConsoleLogCleaner", true);

        var monoSearchOverride = ArgParser.GetValue("melonloader.monosearchpathoverride");
        if (!IsNullOrWhiteSpace(monoSearchOverride))
            SetInternalProperty(config.UnityEngine, "MonoSearchPathOverride", monoSearchOverride);

        if (ArgParser.IsDefined("melonloader.agfoffline"))
            SetInternalProperty(config.UnityEngine, "ForceOfflineGeneration", true);

        var agfRegex = ArgParser.GetValue("melonloader.agfregex");
        if (!IsNullOrWhiteSpace(agfRegex))
            SetInternalProperty(config.UnityEngine, "ForceGeneratorRegex", agfRegex);

        var agfDumper = ArgParser.GetValue("melonloader.agfvdumper");
        if (!IsNullOrWhiteSpace(agfDumper))
            SetInternalProperty(config.UnityEngine, "ForceIl2CppDumperVersion", agfDumper);

        if (ArgParser.IsDefined("melonloader.agfregenerate"))
            SetInternalProperty(config.UnityEngine, "ForceRegeneration", true);

        if (ArgParser.IsDefined("cpp2il.callanalyzer"))
            SetInternalProperty(config.UnityEngine, "EnableCpp2ILCallAnalyzer", true);

        if (ArgParser.IsDefined("cpp2il.nativemethoddetector"))
            SetInternalProperty(config.UnityEngine, "EnableCpp2ILNativeMethodDetector", true);
    }
}
