using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace BepInEx.MelonLoader.Loader.Shared;

/// <summary>
/// Custom AssemblyLoadContext that isolates MelonLoader and its IL2CPP interop assemblies
/// from BepInEx's assemblies, allowing both frameworks to coexist with different assembly versions.
/// </summary>
internal class MelonLoaderAssemblyContext : AssemblyLoadContext
{
    private readonly string _melonLoaderDirectory;
    private readonly string _il2CppAssembliesDirectory;
    private readonly AssemblyDependencyResolver _resolver;

    public MelonLoaderAssemblyContext(string melonLoaderDirectory, string il2CppAssembliesDirectory)
        : base("MelonLoader", isCollectible: false)
    {
        _melonLoaderDirectory = melonLoaderDirectory;
        _il2CppAssembliesDirectory = il2CppAssembliesDirectory;

        // Try to use dependency resolver for the main MelonLoader assembly
        // This may fail in IL2CPP games where hostpolicy isn't initialized
        var melonLoaderDll = Path.Combine(melonLoaderDirectory, "MelonLoader.dll");
        if (File.Exists(melonLoaderDll))
        {
            try
            {
                _resolver = new AssemblyDependencyResolver(melonLoaderDll);
            }
            catch (InvalidOperationException)
            {
                // Hostpolicy not initialized - this is expected in IL2CPP games
                // We'll fall back to manual resolution
                _resolver = null;
            }
        }
    }

    protected override Assembly Load(AssemblyName assemblyName)
    {
        // First, try to load from MelonLoader's Il2CppAssemblies directory
        // This includes Assembly-CSharp and all Unity/Il2Cpp interop assemblies
        var il2CppAssemblyPath = Path.Combine(_il2CppAssembliesDirectory, assemblyName.Name + ".dll");
        if (File.Exists(il2CppAssemblyPath))
        {
            try
            {
                return LoadFromAssemblyPath(il2CppAssemblyPath);
            }
            catch
            {
                // Fall through to next attempt
            }
        }

        // Next, try to load from MelonLoader directory (MelonLoader.dll and dependencies)
        var melonLoaderAssemblyPath = Path.Combine(_melonLoaderDirectory, assemblyName.Name + ".dll");
        if (File.Exists(melonLoaderAssemblyPath))
        {
            try
            {
                return LoadFromAssemblyPath(melonLoaderAssemblyPath);
            }
            catch
            {
                // Fall through to next attempt
            }
        }

        // Try using the dependency resolver
        if (_resolver != null)
        {
            var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (!string.IsNullOrEmpty(assemblyPath) && File.Exists(assemblyPath))
            {
                try
                {
                    return LoadFromAssemblyPath(assemblyPath);
                }
                catch
                {
                    // Fall through to default
                }
            }
        }

        // Let the default context handle it (for system assemblies like System.dll, mscorlib, etc.)
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        // Try to load from MelonLoader Dependencies directory first
        var depsDirectory = Path.Combine(_melonLoaderDirectory, "Dependencies");

        // Try common library name patterns
        var possibleNames = new[]
        {
            unmanagedDllName,
            $"{unmanagedDllName}.dll",
            $"lib{unmanagedDllName}.so",
            $"lib{unmanagedDllName}.dylib"
        };

        foreach (var name in possibleNames)
        {
            var dllPath = Path.Combine(depsDirectory, name);
            if (File.Exists(dllPath))
            {
                try
                {
                    return LoadUnmanagedDllFromPath(dllPath);
                }
                catch
                {
                    // Try next variant
                }
            }
        }

        // Try using the dependency resolver
        if (_resolver != null)
        {
            var dllPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (!string.IsNullOrEmpty(dllPath) && File.Exists(dllPath))
            {
                try
                {
                    return LoadUnmanagedDllFromPath(dllPath);
                }
                catch
                {
                    // Fall through to default
                }
            }
        }

        // Let the default resolution handle it
        return IntPtr.Zero;
    }
}
