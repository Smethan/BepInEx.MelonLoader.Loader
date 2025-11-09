using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using BepInEx.Logging;
using BepInEx.Preloader.Core.Patching;
using Mono.Cecil;

namespace BepInEx.MelonLoader.InteropRedirector
{
    /// <summary>
    /// Pre-patcher that installs an assembly resolution hook to redirect
    /// BepInEx's interop assemblies to MelonLoader's enhanced versions.
    ///
    /// Handles naming differences between BepInEx 6 (.NET Core naming like Il2CppSystem.Private.CoreLib)
    /// and MelonLoader (.NET Framework naming like Il2Cppmscorlib).
    /// </summary>
    [PatcherPluginInfo("com.bepinex.melonloader.interop_redirector", "MelonLoader Interop Redirector", "2.0.0")]
    public class InteropRedirectorPatcher : BasePatcher, IDisposable
    {
        private static ManualLogSource Logger;
        private static string mlInteropPath;
        private static readonly HashSet<string> redirectedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object lockObject = new object();

        // Resolution statistics for debugging
        private static int directMatches;
        private static int aliasMatches;
        private static int assemblyMapMatches;
        private static int lookupFailures;
        private static int resolutionAttempts;

        // Bounded cache for failed lookups to prevent memory leaks
        // Using ConcurrentDictionary for thread-safe cleanup without locking
        private static readonly ConcurrentDictionary<string, byte> failedLookups = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private const int MaxFailedLookupsCache = 500;

        // Maps BepInEx expected names to MelonLoader generated names
        // Discovered at runtime by scanning the Il2CppAssemblies directory
        private static Dictionary<string, string> assemblyNameMap;

        // Known aliases for common assembly naming differences
        // These are fallbacks when assembly discovery doesn't find a match
        private static readonly Dictionary<string, string[]> KnownAliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // .NET Core/5+ naming -> .NET Framework naming
            { "Il2CppSystem.Private.CoreLib", new[] { "Il2Cppmscorlib", "Il2CppCorlib" } },
            { "Il2CppSystem.Runtime", new[] { "Il2CppSystem.Core", "Il2CppSystem" } },
            { "Il2CppSystem.Core", new[] { "Il2CppSystem.Runtime", "Il2CppSystem" } },

            // .NET Framework naming -> .NET Core/5+ naming (reverse lookups)
            { "Il2Cppmscorlib", new[] { "Il2CppSystem.Private.CoreLib", "Il2CppCorlib" } },
            { "Il2CppCorlib", new[] { "Il2CppSystem.Private.CoreLib", "Il2Cppmscorlib" } },
        };

        private bool disposed;

        public override void Initialize()
        {
            Logger = BepInEx.Logging.Logger.CreateLogSource("InteropRedirector");
            Logger.LogInfo("MelonLoader Interop Redirector v2.0 initializing...");

            AssemblyLoadContext.Default.Resolving += OnAssemblyResolving;

            Logger.LogInfo("Assembly resolution hook installed successfully");
            Logger.LogDebug("Enhanced with assembly name mapping and alias resolution");
        }

        public override void Finalizer()
        {
            Logger.LogInfo($"Interop redirection complete. Statistics:");
            Logger.LogInfo($"  Total resolution attempts: {resolutionAttempts}");
            Logger.LogInfo($"  Direct matches: {directMatches}");
            Logger.LogInfo($"  Alias matches: {aliasMatches}");
            Logger.LogInfo($"  Assembly map matches: {assemblyMapMatches}");
            Logger.LogInfo($"  Failed lookups: {lookupFailures}");
            Logger.LogInfo($"  Unique assemblies redirected: {redirectedAssemblies.Count}");

            if (redirectedAssemblies.Count > 0)
            {
                Logger.LogDebug("Redirected assemblies:");
                foreach (var asm in redirectedAssemblies.OrderBy(a => a))
                {
                    Logger.LogDebug($"  - {asm}");
                }
            }
            else
            {
                Logger.LogWarning("No assemblies were redirected. MelonLoader may not have generated assemblies yet.");
                Logger.LogWarning("This is normal on first run. Restart the game for redirection to take effect.");
            }

            // Report on failed lookups if significant
            if (failedLookups.Count > 100)
            {
                Logger.LogWarning($"High number of failed lookups: {failedLookups.Count}");
                Logger.LogWarning("This may indicate missing assemblies or naming mismatches");

                Logger.LogDebug("Failed lookups:");
                foreach (var failed in failedLookups.Keys.Take(20))
                {
                    Logger.LogDebug($"  - {failed}");
                }
                if (failedLookups.Count > 20)
                {
                    Logger.LogDebug($"  ... and {failedLookups.Count - 20} more");
                }
            }
        }

        private static Assembly OnAssemblyResolving(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            lock (lockObject)
            {
                try
                {
                    System.Threading.Interlocked.Increment(ref resolutionAttempts);
                    string simpleName = assemblyName.Name;

                    if (!ShouldRedirect(simpleName))
                    {
                        return null;
                    }

                    // Lazy-initialize MelonLoader interop path and assembly map
                    if (mlInteropPath == null)
                    {
                        mlInteropPath = FindMelonLoaderInteropPath();

                        if (mlInteropPath == null)
                        {
                            Logger.LogWarning("MelonLoader interop path not found. Skipping redirection.");
                            Logger.LogWarning("If this is the first run, MelonLoader needs to generate assemblies.");
                            Logger.LogWarning("The game will use BepInEx assemblies this run, then ML assemblies on next run.");
                            return null;
                        }

                        Logger.LogInfo($"MelonLoader interop path found: {mlInteropPath}");

                        // Build assembly name mapping by scanning available assemblies
                        assemblyNameMap = BuildAssemblyNameMap(mlInteropPath);
                        Logger.LogInfo($"Built assembly name map with {assemblyNameMap.Count} entries");
                    }

                    // Try to resolve with multiple strategies
                    if (TryResolveWithStrategies(simpleName, mlInteropPath, out string resolvedPath, out string matchStrategy))
                    {
                        return LoadAndTrackAssembly(simpleName, resolvedPath, matchStrategy, context);
                    }
                    else
                    {
                        // Cache failed lookup to avoid repeated file system checks
                        CacheFailedLookup(simpleName);
                        Logger.LogDebug($"MelonLoader version of '{simpleName}' not found, using BepInEx version");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error resolving assembly '{assemblyName.Name}': {ex.Message}");
                    Logger.LogDebug($"Stack trace: {ex.StackTrace}");
                }

                return null;
            }
        }

        /// <summary>
        /// Attempts to resolve an assembly using multiple strategies in order of reliability.
        /// </summary>
        private static bool TryResolveWithStrategies(
            string requestedName,
            string mlInteropBasePath,
            out string resolvedPath,
            out string matchStrategy)
        {
            resolvedPath = null;
            matchStrategy = null;

            // Quick exit if we've already failed to find this assembly
            if (failedLookups.ContainsKey(requestedName))
            {
                return false;
            }

            // Strategy 1: Direct match (most reliable)
            string directPath = Path.Combine(mlInteropBasePath, requestedName + ".dll");
            if (File.Exists(directPath))
            {
                resolvedPath = directPath;
                matchStrategy = "DirectMatch";
                System.Threading.Interlocked.Increment(ref directMatches);
                return true;
            }

            // Strategy 2: Assembly name map lookup (discovered from actual assemblies)
            if (assemblyNameMap != null && assemblyNameMap.TryGetValue(requestedName, out string mappedName))
            {
                string mappedPath = Path.Combine(mlInteropBasePath, mappedName + ".dll");
                if (File.Exists(mappedPath))
                {
                    resolvedPath = mappedPath;
                    matchStrategy = $"AssemblyMap({mappedName})";
                    System.Threading.Interlocked.Increment(ref assemblyMapMatches);
                    return true;
                }
            }

            // Strategy 3: Known alias lookup (hardcoded common patterns)
            if (KnownAliases.TryGetValue(requestedName, out string[] aliases))
            {
                foreach (var alias in aliases)
                {
                    string aliasPath = Path.Combine(mlInteropBasePath, alias + ".dll");
                    if (File.Exists(aliasPath))
                    {
                        resolvedPath = aliasPath;
                        matchStrategy = $"Alias({alias})";
                        System.Threading.Interlocked.Increment(ref aliasMatches);
                        return true;
                    }
                }
            }

            // No fuzzy matching - too risky for production use
            // If none of the above strategies work, we should fail explicitly
            System.Threading.Interlocked.Increment(ref lookupFailures);
            return false;
        }

        /// <summary>
        /// Builds a mapping between expected assembly names and actual assembly names
        /// by scanning the Il2CppAssemblies directory and reading assembly metadata.
        /// </summary>
        private static Dictionary<string, string> BuildAssemblyNameMap(string interopPath)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (!Directory.Exists(interopPath))
                {
                    Logger.LogWarning($"Interop path does not exist: {interopPath}");
                    return map;
                }

                var assemblies = Directory.GetFiles(interopPath, "*.dll", SearchOption.TopDirectoryOnly);
                Logger.LogDebug($"Scanning {assemblies.Length} assemblies in {interopPath}");

                foreach (var assemblyPath in assemblies)
                {
                    try
                    {
                        // Use Mono.Cecil for safe metadata reading without loading assemblies
                        using (var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { ReadWrite = false }))
                        {
                            string actualName = Path.GetFileNameWithoutExtension(assemblyPath);
                            string assemblyName = assembly.Name.Name;

                            // If file name differs from assembly name, create mapping
                            if (!string.Equals(actualName, assemblyName, StringComparison.OrdinalIgnoreCase))
                            {
                                // Create bidirectional mapping
                                map[assemblyName] = actualName;
                                map[actualName] = assemblyName;

                                Logger.LogDebug($"Assembly name mapping: {assemblyName} <-> {actualName}");
                            }

                            // Also check for common naming patterns
                            AddCommonMappings(map, actualName, assemblyName);
                        }
                    }
                    catch (BadImageFormatException)
                    {
                        // Skip non-.NET assemblies (native DLLs, etc.)
                        Logger.LogDebug($"Skipping non-managed assembly: {Path.GetFileName(assemblyPath)}");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Failed to read assembly metadata for {Path.GetFileName(assemblyPath)}: {ex.Message}");
                    }
                }

                Logger.LogInfo($"Assembly name map contains {map.Count} mappings");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to build assembly name map: {ex.Message}");
            }

            return map;
        }

        /// <summary>
        /// Adds common naming pattern mappings for known assembly transformations.
        /// </summary>
        private static void AddCommonMappings(Dictionary<string, string> map, string actualName, string assemblyName)
        {
            // Handle mscorlib -> System.Private.CoreLib transformation
            if (actualName.Equals("Il2Cppmscorlib", StringComparison.OrdinalIgnoreCase))
            {
                TryAddMapping(map, "Il2CppSystem.Private.CoreLib", actualName);
                TryAddMapping(map, "Il2CppCorlib", actualName);
            }
            else if (actualName.Equals("Il2CppCorlib", StringComparison.OrdinalIgnoreCase))
            {
                TryAddMapping(map, "Il2CppSystem.Private.CoreLib", actualName);
                TryAddMapping(map, "Il2Cppmscorlib", actualName);
            }

            // Handle System.Core -> System.Runtime transformation
            if (actualName.Equals("Il2CppSystem.Core", StringComparison.OrdinalIgnoreCase))
            {
                TryAddMapping(map, "Il2CppSystem.Runtime", actualName);
            }
            else if (actualName.Equals("Il2CppSystem.Runtime", StringComparison.OrdinalIgnoreCase))
            {
                TryAddMapping(map, "Il2CppSystem.Core", actualName);
            }
        }

        private static void TryAddMapping(Dictionary<string, string> map, string key, string value)
        {
            if (!map.ContainsKey(key))
            {
                map[key] = value;
                Logger.LogDebug($"Added common mapping: {key} -> {value}");
            }
        }

        /// <summary>
        /// Loads an assembly and tracks it for statistics reporting.
        /// Includes error handling for corrupted or incompatible assemblies.
        /// </summary>
        private static Assembly LoadAndTrackAssembly(
            string requestedName,
            string resolvedPath,
            string matchStrategy,
            AssemblyLoadContext context)
        {
            try
            {
                Logger.LogDebug($"Redirecting '{requestedName}' to MelonLoader version at: {resolvedPath}");
                Logger.LogDebug($"  Match strategy: {matchStrategy}");

                // Load MelonLoader's enhanced assembly
                Assembly assembly = context.LoadFromAssemblyPath(resolvedPath);

                // Verify the loaded assembly is actually what we expected
                if (!ValidateLoadedAssembly(assembly, requestedName, resolvedPath))
                {
                    Logger.LogWarning($"Loaded assembly validation failed for '{requestedName}', falling back to default resolution");
                    return null;
                }

                // Track redirected assemblies for reporting
                redirectedAssemblies.Add(requestedName);

                Logger.LogDebug($"Successfully redirected '{requestedName}' (version {assembly.GetName().Version}, strategy: {matchStrategy})");
                return assembly;
            }
            catch (FileLoadException ex)
            {
                Logger.LogError($"Failed to load assembly from '{resolvedPath}': {ex.Message}");
                Logger.LogError("The assembly may be corrupted or incompatible. Falling back to default resolution.");
                CacheFailedLookup(requestedName);
                return null;
            }
            catch (BadImageFormatException ex)
            {
                Logger.LogError($"Assembly at '{resolvedPath}' is not a valid .NET assembly: {ex.Message}");
                CacheFailedLookup(requestedName);
                return null;
            }
        }

        /// <summary>
        /// Validates that a loaded assembly is compatible and expected.
        /// </summary>
        private static bool ValidateLoadedAssembly(Assembly assembly, string requestedName, string assemblyPath)
        {
            try
            {
                // Basic validation: ensure assembly loaded successfully
                if (assembly == null)
                {
                    Logger.LogWarning($"Assembly is null after loading from '{assemblyPath}'");
                    return false;
                }

                // Verify the assembly location matches what we loaded
                if (!string.Equals(assembly.Location, assemblyPath, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.LogWarning($"Assembly location mismatch: expected '{assemblyPath}', got '{assembly.Location}'");
                    return false;
                }

                // Additional validation could go here:
                // - Check for MelonLoader attributes
                // - Verify expected types exist
                // - Check assembly version compatibility

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Assembly validation failed for '{requestedName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Caches a failed lookup with bounded size to prevent memory leaks.
        /// </summary>
        private static void CacheFailedLookup(string assemblyName)
        {
            // Add to cache (thread-safe, no duplicates)
            failedLookups.TryAdd(assemblyName, 0);

            // If cache is too large, clear oldest half
            if (failedLookups.Count > MaxFailedLookupsCache)
            {
                Logger.LogDebug($"Failed lookups cache exceeded {MaxFailedLookupsCache}, clearing oldest entries");

                // Take first half of keys and remove them (approximate LRU)
                var keysToRemove = failedLookups.Keys.Take(MaxFailedLookupsCache / 2).ToList();
                foreach (var key in keysToRemove)
                {
                    failedLookups.TryRemove(key, out _);
                }
            }
        }

        private static bool ShouldRedirect(string assemblyName)
        {
            // Don't redirect .NET framework assemblies
            if (assemblyName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.Equals("System", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Don't redirect BepInEx assemblies
            if (assemblyName.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Don't redirect patching/modding framework assemblies
            if (assemblyName.StartsWith("Mono.Cecil", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("MonoMod", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("0Harmony", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.Equals("HarmonyXInterop", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Don't redirect MelonLoader itself
            if (assemblyName.StartsWith("MelonLoader", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Redirect Unity engine assemblies (unhollowed from IL2CPP)
            if (assemblyName.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("Unity.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Redirect game assemblies (unhollowed from IL2CPP)
            if (assemblyName.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Redirect assemblies with IL2CPP markers in their name
            if (assemblyName.Contains("Il2Cpp", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.Contains("Cpp2IL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static string FindMelonLoaderInteropPath()
        {
            // Strategy 1: Check if MLLoader exists in the BepInEx plugin directory
            string pluginDir = Path.Combine(Paths.PluginPath, "BepInEx.MelonLoader.Loader");
            string mlLoaderInPlugin = Path.Combine(pluginDir, "MLLoader", "MelonLoader", "Il2CppAssemblies");

            if (Directory.Exists(mlLoaderInPlugin))
            {
                Logger.LogDebug($"Found MLLoader in plugin directory: {mlLoaderInPlugin}");
                return mlLoaderInPlugin;
            }

            // Strategy 2: Check game root directory
            string gameRoot = Paths.GameRootPath;
            string mlLoaderInRoot = Path.Combine(gameRoot, "MLLoader", "MelonLoader", "Il2CppAssemblies");

            if (Directory.Exists(mlLoaderInRoot))
            {
                Logger.LogDebug($"Found MLLoader in game root: {mlLoaderInRoot}");
                return mlLoaderInRoot;
            }

            // Strategy 3: Check for alternative plugin subfolder naming
            string altPluginDir = Path.Combine(Paths.PluginPath, "MLLoader", "MelonLoader", "Il2CppAssemblies");
            if (Directory.Exists(altPluginDir))
            {
                Logger.LogDebug($"Found MLLoader in alternate plugin location: {altPluginDir}");
                return altPluginDir;
            }

            // Strategy 4: Search for any MLLoader directory in plugins
            try
            {
                foreach (string subDir in Directory.GetDirectories(Paths.PluginPath))
                {
                    string possiblePath = Path.Combine(subDir, "MLLoader", "MelonLoader", "Il2CppAssemblies");
                    if (Directory.Exists(possiblePath))
                    {
                        Logger.LogDebug($"Found MLLoader via directory search: {possiblePath}");
                        return possiblePath;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Error searching for MLLoader: {ex.Message}");
            }

            Logger.LogWarning("Could not find MelonLoader Il2CppAssemblies directory in any expected location");
            Logger.LogInfo("Searched locations:");
            Logger.LogInfo($"  1. {mlLoaderInPlugin}");
            Logger.LogInfo($"  2. {mlLoaderInRoot}");
            Logger.LogInfo($"  3. {altPluginDir}");
            Logger.LogInfo($"  4. All subdirectories of {Paths.PluginPath}");

            return null;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            try
            {
                // Unhook the event to allow garbage collection
                AssemblyLoadContext.Default.Resolving -= OnAssemblyResolving;
                Logger?.LogDebug("Assembly resolution hook uninstalled");
            }
            catch (Exception ex)
            {
                Logger?.LogWarning($"Error disposing InteropRedirectorPatcher: {ex.Message}");
            }

            disposed = true;
        }
    }
}
