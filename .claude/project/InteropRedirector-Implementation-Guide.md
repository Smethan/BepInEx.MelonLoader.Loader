# BepInEx MelonLoader Interop Redirector - Implementation Guide

## Problem Statement

MelonLoader generates enhanced IL2CPP assemblies with additional APIs, attributes, and extensions that MelonLoader mods depend on. These enhanced assemblies are not present in BepInEx's standard interop assemblies. When both frameworks run together, we need BepInEx to use MelonLoader's enhanced assemblies instead of its own.

## Solution Overview

Use a BepInEx **pre-patcher** that installs an assembly resolution hook using .NET 6+'s `AssemblyLoadContext`. This hook intercepts assembly loading requests and redirects them to MelonLoader's enhanced versions before BepInEx's default interop assemblies are loaded.

### Why Pre-Patcher?

Pre-patchers run in this order:
1. BepInEx Doorstop injection
2. Preloader starts
3. **Pre-patchers execute** ← Our hook installs here
4. Game assemblies load from BepInEx/interop/ ← Our hook intercepts here
5. Chainloader loads plugins
6. MelonLoader plugin initializes

The hook must be installed at step 3 to intercept loading at step 4.

## .NET 6+ AssemblyLoadContext Approach

### Why Use AssemblyLoadContext Over AppDomain.AssemblyResolve?

**Modern Advantages:**
- Better isolation control
- More predictable resolution behavior
- Explicit context management
- Better support for unloadable assemblies
- Aligns with .NET Core/.NET 6+ architecture

**Key Difference:**
```csharp
// Old approach (still works)
AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

// New approach (preferred for .NET 6+)
AssemblyLoadContext.Default.Resolving += OnAssemblyResolving;
```

### When Resolving Event Fires

The `Resolving` event fires when:
1. An assembly is requested via `Assembly.Load()` or reference
2. The assembly is NOT already loaded
3. The default probing paths fail to find it
4. Before throwing `FileNotFoundException`

This is the **perfect interception point** for redirecting to MelonLoader assemblies.

## Complete Implementation

### Project Structure

```
BepInEx.MelonLoader.InteropRedirector/
├── InteropRedirectorPatcher.cs        # Main patcher implementation
├── BepInEx.MelonLoader.InteropRedirector.csproj
└── README.md
```

### BepInEx.MelonLoader.InteropRedirector.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net6.0</TargetFramework>
        <AssemblyName>BepInEx.MelonLoader.InteropRedirector</AssemblyName>
        <Description>Pre-patcher that redirects assembly resolution to MelonLoader's enhanced IL2CPP assemblies</Description>
        <Version>1.0.0</Version>
        <LangVersion>latest</LangVersion>
        <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
        <OutputPath>$(SolutionDir)Output\Patcher\</OutputPath>
    </PropertyGroup>

    <ItemGroup>
        <!-- BepInEx preloader dependencies for BepInEx 6 -->
        <PackageReference Include="BepInEx.Core" Version="6.*" IncludeAssets="compile" />
        <PackageReference Include="BepInEx.Preloader.Core" Version="6.*" IncludeAssets="compile" />
        
        <!-- Mono.Cecil is available in preloader context -->
        <PackageReference Include="Mono.Cecil" Version="0.11.4" IncludeAssets="compile" />
    </ItemGroup>

</Project>
```

### InteropRedirectorPatcher.cs

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using BepInEx.Logging;
using BepInEx.Preloader.Core;
using Mono.Cecil;

namespace BepInEx.MelonLoader.InteropRedirector
{
    /// <summary>
    /// Pre-patcher that installs an assembly resolution hook to redirect
    /// BepInEx's interop assemblies to MelonLoader's enhanced versions.
    /// 
    /// This uses .NET 6+ AssemblyLoadContext for modern, explicit control
    /// over assembly resolution.
    /// </summary>
    [PatcherPluginInfo("com.bepinex.melonloader.interop_redirector", "MelonLoader Interop Redirector", "1.0.0")]
    public class InteropRedirectorPatcher : BasePatcher
    {
        private static ManualLogSource Logger;
        private static string mlInteropPath;
        private static HashSet<string> redirectedAssemblies = new HashSet<string>();
        private static readonly object lockObject = new object();

        /// <summary>
        /// Initialize is called once when the patcher is loaded, before any patching occurs.
        /// This is the perfect place to install assembly resolution hooks.
        /// </summary>
        public override void Initialize()
        {
            Logger = BepInEx.Logging.Logger.CreateLogSource("InteropRedirector");
            Logger.LogInfo("MelonLoader Interop Redirector initializing...");

            // Install the assembly resolution hook using .NET 6+ AssemblyLoadContext
            // This hook will intercept ALL assembly load attempts for the default context
            AssemblyLoadContext.Default.Resolving += OnAssemblyResolving;

            Logger.LogInfo("Assembly resolution hook installed successfully");
            Logger.LogDebug("Hook will redirect Unity/IL2CPP assemblies to MelonLoader versions when available");
        }

        /// <summary>
        /// Finalizer is called after all patching is complete and assemblies are loaded.
        /// Use this to report statistics or clean up.
        /// </summary>
        public override void Finalizer()
        {
            Logger.LogInfo($"Interop redirection complete. Redirected {redirectedAssemblies.Count} assemblies");
            
            if (redirectedAssemblies.Count > 0)
            {
                Logger.LogInfo("Redirected assemblies:");
                foreach (var asm in redirectedAssemblies)
                {
                    Logger.LogInfo($"  - {asm}");
                }
            }
            else
            {
                Logger.LogWarning("No assemblies were redirected. MelonLoader may not have generated assemblies yet.");
                Logger.LogWarning("This is normal on first run. Restart the game for redirection to take effect.");
            }
        }

        /// <summary>
        /// Assembly resolution hook for .NET 6+ AssemblyLoadContext.
        /// 
        /// This event fires when:
        /// 1. An assembly is requested but not already loaded
        /// 2. Default probing paths fail to find it
        /// 3. Before throwing FileNotFoundException
        /// 
        /// Return null to fall back to default resolution.
        /// Return an Assembly to override the resolution.
        /// </summary>
        /// <param name="context">The AssemblyLoadContext requesting the assembly</param>
        /// <param name="assemblyName">The requested assembly name</param>
        /// <returns>The resolved Assembly, or null to use default resolution</returns>
        private static Assembly OnAssemblyResolving(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            // Thread-safe assembly resolution
            lock (lockObject)
            {
                try
                {
                    string simpleName = assemblyName.Name;

                    // Only redirect IL2CPP unhollowed assemblies, not framework assemblies
                    if (!ShouldRedirect(simpleName))
                    {
                        return null; // Let default resolution handle it
                    }

                    // Lazy-initialize the MelonLoader interop path on first relevant assembly request
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
                    }

                    // Check if MelonLoader has an enhanced version of this assembly
                    string mlAssemblyPath = Path.Combine(mlInteropPath, simpleName + ".dll");
                    
                    if (File.Exists(mlAssemblyPath))
                    {
                        Logger.LogDebug($"Redirecting '{simpleName}' to MelonLoader version at: {mlAssemblyPath}");
                        
                        // Load MelonLoader's enhanced assembly instead of BepInEx's version
                        // Use LoadFromAssemblyPath for explicit path loading in the default context
                        Assembly assembly = context.LoadFromAssemblyPath(mlAssemblyPath);
                        
                        // Track redirected assemblies for reporting
                        redirectedAssemblies.Add(simpleName);
                        
                        Logger.LogDebug($"Successfully redirected '{simpleName}' (version {assembly.GetName().Version})");
                        return assembly;
                    }
                    else
                    {
                        Logger.LogDebug($"MelonLoader version of '{simpleName}' not found, using BepInEx version");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error resolving assembly '{assemblyName.Name}': {ex.Message}");
                    Logger.LogDebug($"Stack trace: {ex.StackTrace}");
                }

                // Fall back to default resolution (BepInEx interop assemblies)
                return null;
            }
        }

        /// <summary>
        /// Determines if an assembly should be redirected to MelonLoader's version.
        /// 
        /// We only redirect game and Unity assemblies that have been unhollowed from IL2CPP.
        /// Framework assemblies, BepInEx assemblies, and MelonLoader itself are not redirected.
        /// </summary>
        /// <param name="assemblyName">The simple name of the assembly (without .dll)</param>
        /// <returns>True if the assembly should be redirected, false otherwise</returns>
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

            // For any other assembly, don't redirect (let BepInEx handle it)
            // This is a safe default - we only redirect assemblies we're confident about
            return false;
        }

        /// <summary>
        /// Finds the directory containing MelonLoader's generated IL2CPP assemblies.
        /// 
        /// Searches multiple locations to support different installation methods:
        /// 1. Plugin directory (r2modman/Thunderstore installations)
        /// 2. Game root directory (manual installations)
        /// 3. Symlinked directories (r2modman profile integration)
        /// </summary>
        /// <returns>The path to Il2CppAssemblies directory, or null if not found</returns>
        private static string FindMelonLoaderInteropPath()
        {
            // Strategy 1: Check if MLLoader exists in the BepInEx plugin directory
            // This is the new structure where MLLoader is nested inside the plugin folder
            string pluginDir = Path.Combine(Paths.PluginPath, "BepInEx.MelonLoader.Loader");
            string mlLoaderInPlugin = Path.Combine(pluginDir, "MLLoader", "MelonLoader", "Il2CppAssemblies");
            
            if (Directory.Exists(mlLoaderInPlugin))
            {
                Logger.LogDebug($"Found MLLoader in plugin directory: {mlLoaderInPlugin}");
                return mlLoaderInPlugin;
            }

            // Strategy 2: Check game root directory (for older installations or manual setup)
            string gameRoot = Paths.GameRootPath;
            string mlLoaderInRoot = Path.Combine(gameRoot, "MLLoader", "MelonLoader", "Il2CppAssemblies");
            
            if (Directory.Exists(mlLoaderInRoot))
            {
                Logger.LogDebug($"Found MLLoader in game root: {mlLoaderInRoot}");
                return mlLoaderInRoot;
            }

            // Strategy 3: Check for alternative plugin subfolder naming
            // Some installations might use a different folder structure
            string altPluginDir = Path.Combine(Paths.PluginPath, "MLLoader", "MelonLoader", "Il2CppAssemblies");
            if (Directory.Exists(altPluginDir))
            {
                Logger.LogDebug($"Found MLLoader in alternate plugin location: {altPluginDir}");
                return altPluginDir;
            }

            // Strategy 4: Search for any MLLoader directory in plugins
            // This is a fallback for custom installations
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

            // Not found
            Logger.LogWarning("Could not find MelonLoader Il2CppAssemblies directory in any expected location");
            Logger.LogInfo("Searched locations:");
            Logger.LogInfo($"  1. {mlLoaderInPlugin}");
            Logger.LogInfo($"  2. {mlLoaderInRoot}");
            Logger.LogInfo($"  3. {altPluginDir}");
            Logger.LogInfo($"  4. All subdirectories of {Paths.PluginPath}");
            
            return null;
        }
    }
}
```

## Integration with Build System

### Update Build.cs

Add the patcher compilation and packaging to your existing build script:

```csharp
Target CompilePatcher => _ => _
    .DependsOn(Clean)
    .Executes(() =>
    {
        // Compile the interop redirector patcher
        DotNetBuild(s => s
            .SetProjectFile(RootDirectory / "BepInEx.MelonLoader.InteropRedirector" / "BepInEx.MelonLoader.InteropRedirector.csproj")
            .SetConfiguration("Release")
            .SetFramework("net6.0"));
    });

// Modify your main build to include patcher
Target Compile => _ => _
    .DependsOn(DownloadDependencies, Clean, CompilePatcher)
    .Executes(() =>
    {
        // Your existing build logic...
        HandleBuild("UnityMono", "net35", "BepInEx5", false);
        HandleBuild("UnityMono", "net35", "BepInEx6", false);
        HandleBuild("IL2CPP", "netstandard2.1", "BepInEx6", true);
        
        // Copy patcher to output
        CopyPatcherToOutput();
    });

void CopyPatcherToOutput()
{
    var patcherSource = RootDirectory / "Output" / "Patcher" / "BepInEx.MelonLoader.InteropRedirector.dll";
    
    // IL2CPP builds need the patcher
    var il2cppOutput = OutputDir / "MLLoader-IL2CPP-BepInEx6-2.1.0";
    var patchersDir = il2cppOutput / "BepInEx" / "patchers";
    
    EnsureCleanDirectory(patchersDir);
    CopyFileToDirectory(patcherSource, patchersDir);
    
    Log.Information("Copied interop redirector patcher to IL2CPP build output");
}
```

### Update Solution File

Add the patcher project to your `.sln`:

```
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "BepInEx.MelonLoader.InteropRedirector", "BepInEx.MelonLoader.InteropRedirector\BepInEx.MelonLoader.InteropRedirector.csproj", "{NEW-GUID-HERE}"
EndProject
```

## Package Structure

After building, your package structure should be:

```
MLLoader-IL2CPP-BepInEx6-2.1.0.zip
├── BepInEx/
│   ├── patchers/
│   │   └── BepInEx.MelonLoader.InteropRedirector.dll  ← Pre-patcher goes here
│   └── plugins/
│       └── BepInEx.MelonLoader.Loader/
│           ├── BepInEx.MelonLoader.Loader.IL2CPP.dll
│           ├── MelonLoader.dll
│           ├── 0Harmony.dll
│           └── MLLoader/
│               └── MelonLoader/
│                   ├── Dependencies/
│                   └── Il2CppAssemblies/  ← Generated at runtime
└── icon.png
└── manifest.json
└── README.md
```

## Testing Strategy

### Test 1: First Run Without ML Assemblies

**Expected Behavior:**
```
[Info: InteropRedirector] MelonLoader Interop Redirector initializing...
[Info: InteropRedirector] Assembly resolution hook installed successfully
[Warning: InteropRedirector] MelonLoader interop path not found. Skipping redirection.
[Warning: InteropRedirector] If this is the first run, MelonLoader needs to generate assemblies.
[Info: InteropRedirector] Interop redirection complete. Redirected 0 assemblies
[Warning: InteropRedirector] No assemblies were redirected...
[Warning: InteropRedirector] This is normal on first run. Restart the game...
```

**What Happens:**
- Hook installs successfully
- MelonLoader's Il2CppAssemblies directory doesn't exist yet
- Game loads with BepInEx's interop assemblies
- MelonLoader plugin runs and generates its assemblies
- User should restart the game

### Test 2: Second Run With ML Assemblies

**Expected Behavior:**
```
[Info: InteropRedirector] MelonLoader Interop Redirector initializing...
[Info: InteropRedirector] Assembly resolution hook installed successfully
[Info: InteropRedirector] MelonLoader interop path found: <path>/Il2CppAssemblies
[Debug: InteropRedirector] Redirecting 'UnityEngine.CoreModule' to MelonLoader version
[Debug: InteropRedirector] Successfully redirected 'UnityEngine.CoreModule' (version 0.0.0.0)
[Debug: InteropRedirector] Redirecting 'Assembly-CSharp' to MelonLoader version
[Debug: InteropRedirector] Successfully redirected 'Assembly-CSharp' (version 0.0.0.0)
[Info: InteropRedirector] Interop redirection complete. Redirected 15 assemblies
[Info: InteropRedirector] Redirected assemblies:
[Info: InteropRedirector]   - UnityEngine.CoreModule
[Info: InteropRedirector]   - Assembly-CSharp
[Info: InteropRedirector]   - UnityEngine.UI
...
```

**What Happens:**
- Hook installs successfully
- MelonLoader's assemblies are found
- All Unity/IL2CPP assemblies are redirected to ML versions
- Both BepInEx plugins and MelonLoader mods work correctly

### Test 3: Verify Redirection Works

Create a simple test mod that uses MelonLoader-specific APIs:

```csharp
[BepInPlugin("test.interop.check", "Interop Check", "1.0.0")]
public class InteropCheckPlugin : BasePlugin
{
    public override void Load()
    {
        // Try to access a MelonLoader-enhanced API
        var assembly = Assembly.GetAssembly(typeof(UnityEngine.Object));
        Log.LogInfo($"UnityEngine assembly location: {assembly.Location}");
        
        // Should show MelonLoader's path, not BepInEx's
        if (assembly.Location.Contains("Il2CppAssemblies"))
        {
            Log.LogInfo("✓ Successfully using MelonLoader's enhanced assemblies!");
        }
        else
        {
            Log.LogWarning("✗ Still using BepInEx's assemblies");
        }
    }
}
```

## Key Technical Details

### Thread Safety

The implementation uses `lock (lockObject)` because:
- Multiple assemblies may be requested simultaneously
- `mlInteropPath` is lazily initialized
- `redirectedAssemblies` is mutated during resolution

### Assembly Loading Methods

```csharp
// Preferred: Explicit path in the default context
Assembly assembly = context.LoadFromAssemblyPath(mlAssemblyPath);

// Alternative: Load into default AppDomain (older approach)
Assembly assembly = Assembly.LoadFrom(mlAssemblyPath);
```

We use `LoadFromAssemblyPath` because:
- It's the modern .NET 6+ approach
- Works explicitly with the AssemblyLoadContext
- Provides better control over load location
- Allows potential future isolation if needed

### Why Null Return Falls Back

Returning `null` from the hook tells .NET:
> "I couldn't resolve this assembly, try the next resolver in the chain"

The chain is:
1. Your custom `Resolving` handler
2. Default probing paths (BepInEx/interop/, game directories)
3. `ResolvingUnmanagedDll` for native libraries
4. Throw `FileNotFoundException`

So returning `null` safely falls back to BepInEx's interop assemblies.

## Common Issues and Solutions

### Issue: "Assembly already loaded" errors

**Cause:** Assembly was loaded before hook installed
**Solution:** Ensure patcher is in BepInEx/patchers/, not plugins/

### Issue: No assemblies redirected despite ML directory existing

**Cause:** ML assemblies generated after hook checked
**Solution:** Hook checks lazily on first relevant assembly request

### Issue: Wrong assemblies being redirected

**Cause:** `ShouldRedirect()` logic too broad
**Solution:** Review and tighten the filtering logic

### Issue: Performance concerns

**Impact:** Resolution hook is called frequently
**Mitigation:** 
- Hook only fires when assembly NOT already loaded
- Early returns for non-redirectable assemblies
- Lazy initialization minimizes path checks
- Lock is only held during actual redirection

## Debugging Tips

### Enable Debug Logging

In BepInEx config, set:
```ini
[Logging.Console]
LogLevels = All

[Logging.Disk]
LogLevels = All
```

### Add More Logging

Add this to hook for verbose debugging:
```csharp
Logger.LogDebug($"Resolution requested for: {assemblyName.FullName}");
Logger.LogDebug($"  - Should redirect: {ShouldRedirect(simpleName)}");
Logger.LogDebug($"  - ML path initialized: {mlInteropPath != null}");
```

### Check What's Actually Loaded

After game starts, add this to a plugin:
```csharp
foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
{
    if (assembly.GetName().Name.StartsWith("UnityEngine"))
    {
        Log.LogInfo($"{assembly.GetName().Name} loaded from: {assembly.Location}");
    }
}
```

## Future Enhancements

1. **Configuration File**: Allow users to enable/disable redirection
2. **Blacklist/Whitelist**: Let users specify which assemblies to redirect
3. **Version Checking**: Compare ML vs BepInEx assembly versions
4. **Fallback Strategy**: If ML assembly fails to load, try BepInEx's
5. **Performance Metrics**: Track resolution time and overhead
6. **Custom Load Contexts**: Create isolated context for ML assemblies

## References

- [AssemblyLoadContext Documentation](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblyloadcontext)
- [BepInEx Preloader Patchers Guide](https://docs.bepinex.dev/articles/dev_guide/preloader_patchers.html)
- [Assembly Loading in .NET](https://docs.microsoft.com/en-us/dotnet/core/dependency-loading/overview)

## Summary

This approach provides:
- ✅ Automatic redirection of IL2CPP assemblies to MelonLoader versions
- ✅ No manual file copying required
- ✅ Safe fallback to BepInEx assemblies
- ✅ Works with r2modman and manual installations
- ✅ First-run friendly
- ✅ Comprehensive logging for debugging
- ✅ Modern .NET 6+ architecture

The patcher installs early, intercepts assembly loading transparently, and ensures both BepInEx plugins and MelonLoader mods use the same enhanced assemblies.
