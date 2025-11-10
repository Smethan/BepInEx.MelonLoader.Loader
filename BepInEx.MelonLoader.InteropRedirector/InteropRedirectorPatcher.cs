using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using BepInEx.Logging;
using BepInEx.Preloader.Core.Patching;
using HarmonyLib;
using Mono.Cecil;

namespace BepInEx.MelonLoader.InteropRedirector
{
    /// <summary>
    /// Pre-patcher that installs an assembly resolution hook to redirect
    /// BepInEx's interop assemblies to MelonLoader's enhanced versions.
    ///
    /// Handles naming differences between BepInEx 6 (.NET Core naming like Il2CppSystem.Private.CoreLib)
    /// and MelonLoader (.NET Framework naming like Il2Cppmscorlib).
    ///
    /// v2.1: Enhanced with thread-safety fixes, ALC isolation awareness, and proper disposal.
    /// v2.2: Security hardening (path traversal protection, DoS prevention, bounded caching).
    /// v2.3: Critical fix - Harmony patch to bypass .NET assembly name validation for IL2CPP.
    /// </summary>
    [PatcherPluginInfo("com.bepinex.melonloader.interop_redirector", "MelonLoader Interop Redirector", "2.3.0")]
    public class InteropRedirectorPatcher : BasePatcher, IDisposable
    {
        private static ManualLogSource Logger;
        private static readonly HashSet<string> redirectedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object lockObject = new object();

        // P0 #1: Harmony instance for patching .NET validation
        private static Harmony harmony;

        // Resolution statistics for debugging
        private static int directMatches;
        private static int aliasMatches;
        private static int assemblyMapMatches;
        private static int alcReuseMatches;
        private static int lookupFailures;
        private static int resolutionAttempts;

        // Bounded cache for failed lookups to prevent memory leaks
        // Using ConcurrentDictionary for thread-safe cleanup without locking
        private static readonly ConcurrentDictionary<string, byte> failedLookups = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private const int MaxFailedLookupsCache = 500;
        private const int MaxAlcScanCount = 50;  // Circuit breaker for ALC scanning
        private const int MaxLoadedAssemblyCacheSize = 1000;  // FIX #8: Bounded cache size

        // Cache of already-loaded assemblies across all ALCs for fast lookup
        // Key: assembly simple name, Value: (ALC, Assembly)
        private static readonly ConcurrentDictionary<string, Tuple<AssemblyLoadContext, Assembly>> loadedAssemblyCache =
            new ConcurrentDictionary<string, Tuple<AssemblyLoadContext, Assembly>>(StringComparer.OrdinalIgnoreCase);

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

        // FIX #1: Thread-safe lazy initialization using Lazy<T>
        // This replaces the double-checked locking pattern with guaranteed thread-safe initialization
        // NOTE: Not readonly so NotifyMelonLoaderReady() can replace it for re-initialization
        private static Lazy<InitializationState> lazyState = new Lazy<InitializationState>(
            () => InitializeMelonLoaderState(),
            System.Threading.LazyThreadSafetyMode.ExecutionAndPublication
        );

        // FIX #2: Track event handler installation for proper disposal
        private volatile bool _handlerInstalled;
        private bool disposed;

        private class InitializationState
        {
            public string MlInteropPath { get; set; }
            public Dictionary<string, string> AssemblyNameMap { get; set; }
            public bool IsInitialized { get; set; }
        }

        public override void Initialize()
        {
            Logger = BepInEx.Logging.Logger.CreateLogSource("InteropRedirector");
            Logger.LogInfo("MelonLoader Interop Redirector v2.3 initializing...");
            Logger.LogDebug("Enhanced with thread-safety, ALC isolation, and security hardening");

            try
            {
                // P0 #1: Patch .NET's internal assembly name validation BEFORE installing the resolver hook
                // This allows returning Il2Cppmscorlib.dll when Il2CppSystem.Private.CoreLib is requested
                harmony = new Harmony("com.bepinex.melonloader.interop_redirector");

                var validateMethod = typeof(AssemblyLoadContext).GetMethod(
                    "ValidateAssemblyNameWithSimpleName",
                    BindingFlags.Static | BindingFlags.NonPublic);

                if (validateMethod != null)
                {
                    harmony.Patch(validateMethod,
                        prefix: new HarmonyMethod(typeof(InteropRedirectorPatcher), nameof(BypassIl2CppNameValidation)));
                    Logger.LogInfo("Assembly name validation bypass installed");
                }
                else
                {
                    Logger.LogWarning("Could not find ValidateAssemblyNameWithSimpleName - .NET version may be incompatible");
                }

                // NOTE: Assembly resolution hook will be installed in Finalizer()
                // This allows BepInEx to preload its interop assemblies first
                Logger.LogInfo("Harmony patches installed. Resolution hook will be activated in Finalizer phase.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to install Harmony patches: {ex.Message}");
                throw;
            }
        }

        public override void Finalizer()
        {
            // Install assembly resolution hook AFTER BepInEx has preloaded its interop assemblies
            // This prevents interfering with BepInEx's own initialization
            try
            {
                AssemblyLoadContext.Default.Resolving += OnAssemblyResolving;
                _handlerInstalled = true;
                Logger.LogInfo("Assembly resolution hook installed successfully");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to install resolution hook: {ex.Message}");
                _handlerInstalled = false;
            }

            Logger.LogInfo("InteropRedirector preloader phase complete.");
            Logger.LogDebug("Assembly resolution handler is active. Statistics will be reported at process shutdown.");
        }

        /// <summary>
        /// Called by the MelonLoader plugin after MelonLoader finishes generating assemblies.
        /// Forces re-initialization of the InteropRedirector state by replacing the Lazy instance.
        /// This ensures we check for assemblies AFTER they've been generated, not before.
        /// </summary>
        public static void NotifyMelonLoaderReady()
        {
            Logger.LogInfo("Re-initializing InteropRedirector state after MelonLoader generation...");

            // Force re-initialization by replacing the Lazy instance
            // This throws away any cached failed state from early initialization attempts
            lazyState = new Lazy<InitializationState>(
                () => InitializeMelonLoaderState(),
                System.Threading.LazyThreadSafetyMode.ExecutionAndPublication
            );

            // Now evaluate the fresh lazy state
            var state = lazyState.Value;

            if (state?.IsInitialized == true)
            {
                Logger.LogInfo($"✓ MelonLoader path confirmed: {state.MlInteropPath}");
                Logger.LogInfo($"✓ InteropRedirector ready to redirect {state.AssemblyNameMap.Count} assembly mappings");

                // Create filesystem aliases so MelonLoader's resolver can find assemblies by their expected names
                CreateAssemblyAliases(state.MlInteropPath);
            }
            else
            {
                Logger.LogWarning("✗ MelonLoader assemblies still not found after generation");
                Logger.LogWarning("This should not happen - assembly redirection may not work correctly");
            }
        }

        /// <summary>
        /// Creates filesystem aliases for assemblies with mismatched names.
        /// This allows MelonLoader's resolver (which does exact filename matching) to find assemblies
        /// even when the requested name doesn't match the actual filename.
        /// </summary>
        private static void CreateAssemblyAliases(string il2CppAssembliesPath)
        {
            var aliases = new Dictionary<string, string>
            {
                { "Il2CppSystem.Private.CoreLib.dll", "Il2Cppmscorlib.dll" },
                { "Il2CppCorlib.dll", "Il2Cppmscorlib.dll" },
                { "Il2CppSystem.Runtime.dll", "Il2CppSystem.Core.dll" }
            };

            Logger.LogInfo("Creating assembly filename aliases for MelonLoader's resolver...");
            int successCount = 0;

            foreach (var kvp in aliases)
            {
                string aliasPath = Path.Combine(il2CppAssembliesPath, kvp.Key);
                string targetPath = Path.Combine(il2CppAssembliesPath, kvp.Value);

                // Skip if alias already exists or target doesn't exist
                if (File.Exists(aliasPath))
                {
                    Logger.LogDebug($"Alias already exists: {kvp.Key}");
                    successCount++;
                    continue;
                }

                if (!File.Exists(targetPath))
                {
                    Logger.LogWarning($"Target file not found, cannot create alias: {kvp.Value}");
                    continue;
                }

                try
                {
                    // Copy the file with the alias name
                    File.Copy(targetPath, aliasPath, overwrite: false);
                    Logger.LogInfo($"✓ Created alias: {kvp.Key} -> {kvp.Value}");
                    successCount++;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Failed to create alias for {kvp.Key}: {ex.Message}");
                }
            }

            Logger.LogInfo($"Assembly aliases created: {successCount}/{aliases.Count}");
        }

        // P1 #5: Reduced lock granularity - only lock when modifying shared state
        private static Assembly OnAssemblyResolving(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            try
            {
                System.Threading.Interlocked.Increment(ref resolutionAttempts);
                string simpleName = assemblyName.Name;

                // Read-only checks - no lock needed
                if (!ShouldRedirect(simpleName))
                    return null;

                // Check cache - concurrent dictionary is thread-safe
                var existingAssembly = FindAssemblyInAnyContext(simpleName);
                if (existingAssembly != null)
                {
                    System.Threading.Interlocked.Increment(ref alcReuseMatches);
                    Logger.LogDebug($"Assembly '{simpleName}' already loaded in {existingAssembly.Item1.Name}, reusing");

                    // Only lock for adding to redirectedAssemblies set
                    lock (lockObject)
                    {
                        redirectedAssemblies.Add(simpleName);
                    }
                    return existingAssembly.Item2;
                }

                // Access lazy state (thread-safe)
                var state = lazyState.Value;
                if (state?.IsInitialized != true)
                {
                    Logger.LogWarning("MelonLoader initialization failed. Skipping redirection.");
                    return null;
                }

                // Try to resolve - read-only operations, no lock needed
                if (TryResolveWithStrategies(simpleName, state.MlInteropPath, state.AssemblyNameMap,
                    out string resolvedPath, out string matchStrategy))
                {
                    // Lock only for the actual assembly loading and tracking
                    lock (lockObject)
                    {
                        return LoadAndTrackAssembly(simpleName, resolvedPath, matchStrategy, context);
                    }
                }
                else
                {
                    // Cache failed lookup
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

        /// <summary>
        /// FIX #3: Searches for an assembly across all registered AssemblyLoadContexts.
        /// Prevents loading the same assembly multiple times in different contexts, which causes
        /// type conflicts and InvalidCastException errors.
        ///
        /// Uses a cache to optimize repeated lookups.
        /// </summary>
        private static Tuple<AssemblyLoadContext, Assembly> FindAssemblyInAnyContext(string assemblyName)
        {
            // First check the cache for fast lookup
            if (loadedAssemblyCache.TryGetValue(assemblyName, out var cached))
            {
                try
                {
                    // Verify both assembly and ALC are still valid
                    _ = cached.Item2.FullName;
                    _ = cached.Item1.Name; // Verify ALC is still accessible

                    // If ALC is collectible, verify it still contains the assembly
                    if (cached.Item1.IsCollectible && !cached.Item1.Assemblies.Contains(cached.Item2))
                    {
                        Logger.LogDebug($"Cached assembly '{assemblyName}' no longer in its ALC, removing from cache");
                        loadedAssemblyCache.TryRemove(assemblyName, out _);
                    }
                    else
                    {
                        return cached;
                    }
                }
                catch
                {
                    // Assembly or ALC was unloaded, remove from cache
                    loadedAssemblyCache.TryRemove(assemblyName, out _);
                }
            }

            try
            {
                // Check Default context first (most common case)
                var defaultAssembly = AssemblyLoadContext.Default.Assemblies
                    .FirstOrDefault(a => a.GetName().Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase));

                if (defaultAssembly != null)
                {
                    var result = Tuple.Create(AssemblyLoadContext.Default, defaultAssembly);
                    // FIX #8: Add to cache with size limit
                    AddToLoadedAssemblyCache(assemblyName, result);
                    return result;
                }

                // FIX #6: Check all other contexts with circuit breaker protection
                int scannedCount = 0;
                foreach (var alc in AssemblyLoadContext.All)
                {
                    if (alc == AssemblyLoadContext.Default)
                        continue;

                    // Circuit breaker: prevent DoS from excessive ALC scanning
                    if (++scannedCount > MaxAlcScanCount)
                    {
                        Logger.LogWarning($"ALC scan limit reached ({MaxAlcScanCount}). " +
                            "System has excessive AssemblyLoadContexts - possible DoS attempt or runaway plugin. " +
                            $"Assembly '{assemblyName}' not found in scanned contexts.");
                        break;
                    }

                    try
                    {
                        // Validate ALC is still alive before accessing
                        if (alc.IsCollectible)
                        {
                            // Check if ALC has any assemblies (quick collectible check)
                            var alcAssemblies = alc.Assemblies;
                            if (!alcAssemblies.Any())
                                continue;
                        }

                        var assembly = alc.Assemblies
                            .FirstOrDefault(a => a.GetName().Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase));

                        if (assembly != null)
                        {
                            var result = Tuple.Create(alc, assembly);

                            // FIX #8: Add to cache with size limit
                            AddToLoadedAssemblyCache(assemblyName, result);
                            return result;
                        }
                    }
                    catch (Exception ex)
                    {
                        // ALC was unloaded during iteration - this is expected in some scenarios
                        Logger.LogDebug($"ALC became invalid during enumeration: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"Error searching for assembly '{assemblyName}' in contexts: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// P0 #1: Harmony prefix patch that bypasses .NET's assembly name validation for Il2CPP assemblies.
        /// Allows returning Il2Cppmscorlib.dll when Il2CppSystem.Private.CoreLib is requested.
        /// This is required because .NET 6+ has internal validation that rejects assemblies whose
        /// simple name doesn't match the requested name, even if returned from a Resolving handler.
        /// </summary>
        private static bool BypassIl2CppNameValidation(Assembly assembly, string requestedSimpleName)
        {
            try
            {
                string actualName = assembly.GetName().Name;

                // Only bypass validation for Il2CPP-related assemblies
                if (!requestedSimpleName.StartsWith("Il2Cpp", StringComparison.OrdinalIgnoreCase))
                    return true; // Run normal validation

                // Check if this is a known Il2CPP name mapping
                if (IsKnownIl2CppMapping(requestedSimpleName, actualName))
                {
                    Logger?.LogDebug($"Bypassing .NET validation: '{requestedSimpleName}' -> '{actualName}'");
                    return false; // Skip validation (prefix returns false = don't run original)
                }

                return true; // Run normal validation
            }
            catch (Exception ex)
            {
                Logger?.LogError($"Error in validation bypass: {ex.Message}");
                return true; // Fall back to normal validation on error
            }
        }

        /// <summary>
        /// P0 #1: Checks if two assembly names are known aliases in the Il2CPP context.
        /// Used by the Harmony validation bypass to determine which name mismatches are acceptable.
        /// </summary>
        private static bool IsKnownIl2CppMapping(string requested, string actual)
        {
            // Check bidirectional mapping in KnownAliases
            if (KnownAliases.TryGetValue(requested, out var aliases) &&
                aliases.Any(a => a.Equals(actual, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (KnownAliases.TryGetValue(actual, out aliases) &&
                aliases.Any(a => a.Equals(requested, StringComparison.OrdinalIgnoreCase)))
                return true;

            // Check assembly name map if initialized
            if (lazyState.IsValueCreated)
            {
                var state = lazyState.Value;
                if (state?.AssemblyNameMap != null)
                {
                    if (state.AssemblyNameMap.TryGetValue(requested, out string mapped) &&
                        mapped.Equals(actual, StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (state.AssemblyNameMap.TryGetValue(actual, out mapped) &&
                        mapped.Equals(requested, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Thread-safe initialization of MelonLoader state.
        /// Called exactly once by Lazy<T>.
        /// </summary>
        private static InitializationState InitializeMelonLoaderState()
        {
            try
            {
                var path = FindMelonLoaderInteropPath();

                if (path == null)
                {
                    Logger.LogWarning("MelonLoader interop path not found. Skipping redirection.");
                    Logger.LogWarning("If this is the first run, MelonLoader needs to generate assemblies.");
                    Logger.LogWarning("The game will use BepInEx assemblies this run, then ML assemblies on next run.");
                    return new InitializationState { IsInitialized = false };
                }

                Logger.LogInfo($"MelonLoader interop path found: {path}");

                // Build assembly name mapping by scanning available assemblies
                var map = BuildAssemblyNameMap(path);
                Logger.LogInfo($"Built assembly name map with {map.Count} entries");

                return new InitializationState
                {
                    MlInteropPath = path,
                    AssemblyNameMap = map,
                    IsInitialized = true
                };
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to initialize MelonLoader state: {ex.Message}");
                Logger.LogDebug($"Stack trace: {ex.StackTrace}");
                return new InitializationState { IsInitialized = false };
            }
        }

        /// <summary>
        /// Attempts to resolve an assembly using multiple strategies in order of reliability.
        /// </summary>
        private static bool TryResolveWithStrategies(
            string requestedName,
            string mlInteropBasePath,
            Dictionary<string, string> assemblyNameMap,
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
            // FIX #7: Validate path is within allowed directory
            if (IsPathWithinDirectory(directPath, mlInteropBasePath) && File.Exists(directPath))
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
                // FIX #7: Validate path is within allowed directory
                if (IsPathWithinDirectory(mappedPath, mlInteropBasePath) && File.Exists(mappedPath))
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
                    // FIX #7: Validate path is within allowed directory
                    if (IsPathWithinDirectory(aliasPath, mlInteropBasePath) && File.Exists(aliasPath))
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
        /// Updates the loaded assembly cache to prevent ALC duplication.
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

                // FIX #8: Add to loaded assembly cache with size limit
                AddToLoadedAssemblyCache(requestedName, Tuple.Create(context, assembly));

                // P0 #2: More accurate logging - .NET still validates after we return
                Logger.LogDebug($"Loaded '{requestedName}' from MelonLoader path (version {assembly.GetName().Version}, strategy: {matchStrategy})");
                Logger.LogDebug($"Note: .NET will validate assembly name after this handler returns");
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

                // P1 #3: CRITICAL - Validate assembly name matches what was requested
                // If names don't match, .NET's internal validation will reject it after we return
                string assemblySimpleName = assembly.GetName().Name;
                if (!assemblySimpleName.Equals(requestedName, StringComparison.OrdinalIgnoreCase))
                {
                    // Check if this is a known mapping that will be handled by Harmony patch
                    if (!IsKnownIl2CppMapping(requestedName, assemblySimpleName))
                    {
                        Logger.LogWarning($"Assembly name mismatch: requested '{requestedName}', loaded '{assemblySimpleName}'");
                        Logger.LogWarning("This will fail .NET validation unless Harmony patch is active");
                        return false;
                    }
                    else
                    {
                        Logger.LogDebug($"Assembly name mismatch detected but allowed by Harmony patch: '{requestedName}' -> '{assemblySimpleName}'");
                    }
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
            // FIX #7: SECURITY - Validate assembly name format to prevent path traversal
            if (!IsValidAssemblyName(assemblyName))
            {
                Logger.LogWarning($"Rejecting invalid assembly name: '{assemblyName}' (possible path traversal attempt)");
                return false;
            }

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
            // Strategy 0: Detect plugin directory from InteropRedirector assembly location
            // This works regardless of r2modman naming or manual installation
            try
            {
                var thisAssembly = Assembly.GetExecutingAssembly();
                var assemblyDir = Path.GetDirectoryName(thisAssembly.Location);
                Logger.LogDebug($"InteropRedirector assembly location: {assemblyDir}");

                // InteropRedirector is deployed to BepInEx/patchers/
                // We need to navigate to BepInEx/plugins/ and search for MLLoader
                var bepInExDir = Directory.GetParent(assemblyDir)?.FullName;
                if (bepInExDir != null)
                {
                    var pluginsDir = Path.Combine(bepInExDir, "plugins");
                    if (Directory.Exists(pluginsDir))
                    {
                        Logger.LogDebug($"Searching for MLLoader in plugins directory: {pluginsDir}");

                        // Search all plugin folders for MLLoader structure
                        foreach (var pluginFolder in Directory.GetDirectories(pluginsDir))
                        {
                            try
                            {
                                string possiblePath = Path.Combine(pluginFolder, "MLLoader", "MelonLoader", "Il2CppAssemblies");
                                if (Directory.Exists(possiblePath))
                                {
                                    Logger.LogInfo($"Found MLLoader via assembly location in {Path.GetFileName(pluginFolder)}");
                                    return possiblePath;
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.LogDebug($"  Error checking plugin folder '{Path.GetFileName(pluginFolder)}': {ex.Message}");
                            }
                        }

                        Logger.LogDebug("MLLoader not found in any plugin folder via assembly location strategy");
                    }
                    else
                    {
                        Logger.LogDebug($"Plugins directory does not exist: {pluginsDir}");
                    }
                }
                else
                {
                    Logger.LogDebug("Could not determine BepInEx directory from assembly location");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to detect MLLoader from assembly location: {ex.Message}");
            }

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

            // Strategy 4: Search for any MLLoader directory in plugins (with enhanced logging)
            try
            {
                var pluginDirs = Directory.GetDirectories(Paths.PluginPath);
                Logger.LogDebug($"Strategy 4: Scanning {pluginDirs.Length} plugin directories for MLLoader...");

                foreach (string subDir in pluginDirs)
                {
                    try
                    {
                        string dirName = Path.GetFileName(subDir);
                        string possiblePath = Path.Combine(subDir, "MLLoader", "MelonLoader", "Il2CppAssemblies");

                        bool exists = Directory.Exists(possiblePath);
                        Logger.LogDebug($"  Checking {dirName}: {(exists ? "FOUND" : "not found")}");

                        if (exists)
                        {
                            Logger.LogInfo($"Found MLLoader via directory search in {dirName}");
                            return possiblePath;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"  Error checking plugin directory '{Path.GetFileName(subDir)}': {ex.Message}");
                        // Continue to next directory
                    }
                }

                Logger.LogWarning($"Strategy 4: Scanned all {pluginDirs.Length} plugin directories but did not find MLLoader");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Strategy 4: Critical error searching for MLLoader: {ex.Message}");
                Logger.LogDebug($"Stack trace: {ex.StackTrace}");
            }

            Logger.LogWarning("Could not find MelonLoader Il2CppAssemblies directory in any expected location");
            Logger.LogInfo("Searched locations:");
            Logger.LogInfo($"  1. {mlLoaderInPlugin}");
            Logger.LogInfo($"  2. {mlLoaderInRoot}");
            Logger.LogInfo($"  3. {altPluginDir}");
            Logger.LogInfo($"  4. All subdirectories of {Paths.PluginPath}");

            return null;
        }

        /// <summary>
        /// FIX #8: Adds an assembly to the loaded assembly cache with bounded size enforcement.
        /// Implements LRU-style eviction when cache is full to prevent memory exhaustion.
        /// </summary>
        private static void AddToLoadedAssemblyCache(string assemblyName, Tuple<AssemblyLoadContext, Assembly> value)
        {
            if (loadedAssemblyCache.Count < MaxLoadedAssemblyCacheSize)
            {
                loadedAssemblyCache.TryAdd(assemblyName, value);
            }
            else
            {
                Logger.LogDebug($"Loaded assembly cache full ({MaxLoadedAssemblyCacheSize}), evicting 25% of entries");

                // Evict 25% of oldest entries
                var keysToRemove = loadedAssemblyCache.Keys
                    .Take(MaxLoadedAssemblyCacheSize / 4)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    loadedAssemblyCache.TryRemove(key, out _);
                }

                loadedAssemblyCache.TryAdd(assemblyName, value);
            }
        }

        /// <summary>
        /// FIX #7: Validates that an assembly name doesn't contain path traversal sequences or invalid characters.
        /// Prevents malicious assembly names from accessing files outside the intended directory.
        /// </summary>
        private static bool IsValidAssemblyName(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
                return false;

            // Reject path traversal sequences
            if (assemblyName.Contains("..") ||
                assemblyName.Contains("/") ||
                assemblyName.Contains("\\") ||
                assemblyName.Contains(":"))
            {
                return false;
            }

            // Reject invalid filename characters
            char[] invalidChars = Path.GetInvalidFileNameChars();
            if (assemblyName.IndexOfAny(invalidChars) >= 0)
            {
                return false;
            }

            // Reject excessively long names
            if (assemblyName.Length > 255)
                return false;

            return true;
        }

        /// <summary>
        /// FIX #7: Validates that a resolved path is within the allowed base directory.
        /// Prevents path traversal attacks that could access arbitrary files on the system.
        /// </summary>
        private static bool IsPathWithinDirectory(string path, string baseDirectory)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                string fullBaseDir = Path.GetFullPath(baseDirectory);

                if (!fullBaseDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    fullBaseDir += Path.DirectorySeparatorChar;
                }

                return fullPath.StartsWith(fullBaseDir, StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Path validation failed for '{path}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// FIX #2: Proper disposal with comprehensive cleanup.
        /// Ensures event handler is unhooked and all caches are cleared to prevent memory leaks.
        /// </summary>
        public void Dispose()
        {
            if (disposed)
                return;

            try
            {
                // Print final statistics BEFORE cleanup
                Logger?.LogInfo("===== InteropRedirector Final Statistics =====");
                Logger?.LogInfo($"  Total resolution attempts: {resolutionAttempts}");
                Logger?.LogInfo($"  Direct matches: {directMatches}");
                Logger?.LogInfo($"  Alias matches: {aliasMatches}");
                Logger?.LogInfo($"  Assembly map matches: {assemblyMapMatches}");
                Logger?.LogInfo($"  ALC reuse matches: {alcReuseMatches}");
                Logger?.LogInfo($"  Failed lookups: {lookupFailures}");
                Logger?.LogInfo($"  Unique assemblies redirected: {redirectedAssemblies.Count}");

                if (redirectedAssemblies.Count > 0)
                {
                    Logger?.LogDebug("Redirected assemblies:");
                    foreach (var asm in redirectedAssemblies.OrderBy(a => a))
                    {
                        Logger?.LogDebug($"  - {asm}");
                    }
                }
                else
                {
                    Logger?.LogWarning("No assemblies were redirected. MelonLoader may not have generated assemblies yet.");
                    Logger?.LogWarning("This is normal on first run. Restart the game for redirection to take effect.");
                }

                // Report on failed lookups if significant
                if (failedLookups.Count > 100)
                {
                    Logger?.LogWarning($"High number of failed lookups: {failedLookups.Count}");
                    Logger?.LogWarning("This may indicate missing assemblies or naming mismatches");

                    Logger?.LogDebug("Failed lookups:");
                    foreach (var failed in failedLookups.Keys.Take(20))
                    {
                        Logger?.LogDebug($"  - {failed}");
                    }
                    if (failedLookups.Count > 20)
                    {
                        Logger?.LogDebug($"  ... and {failedLookups.Count - 20} more");
                    }
                }

                // Report on loaded assembly cache
                Logger?.LogDebug($"Loaded assembly cache contains {loadedAssemblyCache.Count} entries");
                Logger?.LogInfo("===============================================");

                // Only unhook if we actually installed the handler
                if (_handlerInstalled)
                {
                    AssemblyLoadContext.Default.Resolving -= OnAssemblyResolving;
                    _handlerInstalled = false;
                    Logger?.LogDebug("Assembly resolution hook uninstalled");
                }

                // P0 #1: Unpatch Harmony modifications before clearing caches
                harmony?.UnpatchSelf();
                Logger?.LogDebug("Harmony patches removed");

                // Clear all caches to allow garbage collection
                failedLookups.Clear();
                redirectedAssemblies.Clear();
                loadedAssemblyCache.Clear();

                // Note: We cannot clear the lazy state or assembly name map
                // as they are static and shared across all instances
                // This is acceptable since there should only be one instance per process

                Logger?.LogInfo("InteropRedirector disposed successfully");
            }
            catch (Exception ex)
            {
                Logger?.LogWarning($"Error disposing InteropRedirectorPatcher: {ex.Message}");
            }

            disposed = true;
        }
    }
}
