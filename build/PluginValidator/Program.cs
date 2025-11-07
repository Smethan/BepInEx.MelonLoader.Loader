using System;
using System.IO;
using System.Linq;
using System.Reflection;

class Program
{
    static int Main(string[] args)
    {
        var dllPath = args.Length > 0 ? args[0] : 
            "../../BepInEx.MelonLoader.Loader.UnityMono/Output/BepInEx5/UnityMono/BepInEx.MelonLoader.Loader.UnityMono.dll";
        
        dllPath = Path.GetFullPath(dllPath);
        
        Console.WriteLine("BepInEx Plugin Validator");
        Console.WriteLine("========================");
        Console.WriteLine($"Analyzing: {Path.GetFileName(dllPath)}");
        Console.WriteLine($"Full path: {dllPath}");
        Console.WriteLine();
        
        if (!File.Exists(dllPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"ERROR: File not found: {dllPath}");
            Console.ResetColor();
            return 1;
        }
        
        try
        {
            // Load assembly for reflection
            var assembly = Assembly.LoadFrom(dllPath);
            
            Console.WriteLine($"Assembly: {assembly.FullName}");
            Console.WriteLine($"Target Framework: {assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName ?? "Unknown"}");
            Console.WriteLine();
            
            var types = assembly.GetTypes();
            Console.WriteLine($"Total types found: {types.Length}");
            Console.WriteLine();
            
            // Look for BepInPlugin attributes
            bool foundPlugin = false;
            int pluginCount = 0;
            
            foreach (var type in types)
            {
                var attributes = type.GetCustomAttributes(false);
                var bepInPluginAttr = attributes.FirstOrDefault(a => 
                    a.GetType().Name == "BepInPlugin" || 
                    a.GetType().Name == "BepInPluginAttribute");
                
                if (bepInPluginAttr != null)
                {
                    pluginCount++;
                    foundPlugin = true;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✓ Found BepInPlugin #{pluginCount}: {type.FullName}");
                    Console.ResetColor();
                    
                    // Try to extract GUID, Name, Version using reflection
                    var attrType = bepInPluginAttr.GetType();
                    
                    try
                    {
                        var guidField = attrType.GetField("GUID");
                        var nameField = attrType.GetField("Name");
                        var versionField = attrType.GetField("Version");
                        
                        if (guidField != null)
                            Console.WriteLine($"  GUID: {guidField.GetValue(bepInPluginAttr)}");
                        if (nameField != null)
                            Console.WriteLine($"  Name: {nameField.GetValue(bepInPluginAttr)}");
                        if (versionField != null)
                            Console.WriteLine($"  Version: {versionField.GetValue(bepInPluginAttr)}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  (Could not read attribute properties: {ex.Message})");
                    }
                    
                    // Check base class
                    var baseType = type.BaseType;
                    Console.WriteLine($"  Base Type: {baseType?.FullName ?? "None"}");
                    
                    if (baseType != null)
                    {
                        if (baseType.Name.Contains("BaseUnityPlugin") || baseType.Name.Contains("BasePlugin"))
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"  ✓ Inherits from BepInEx base class");
                            Console.ResetColor();
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"  ⚠ Warning: Doesn't inherit from expected BepInEx base");
                            Console.ResetColor();
                        }
                    }
                    
                    // Check for common methods
                    var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    var awakeMethod = methods.FirstOrDefault(m => m.Name == "Awake");
                    var loadMethod = methods.FirstOrDefault(m => m.Name == "Load");
                    var startMethod = methods.FirstOrDefault(m => m.Name == "Start");
                    var updateMethod = methods.FirstOrDefault(m => m.Name == "Update");
                    
                    if (awakeMethod != null)
                        Console.WriteLine($"  ✓ Has Awake() method");
                    if (loadMethod != null)
                        Console.WriteLine($"  ✓ Has Load() method");
                    if (startMethod != null)
                        Console.WriteLine($"  ✓ Has Start() method");
                    if (updateMethod != null)
                        Console.WriteLine($"  ✓ Has Update() method");
                    
                    Console.WriteLine();
                }
            }
            
            if (!foundPlugin)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("✗ ERROR: No BepInPlugin attribute found!");
                Console.WriteLine("  BepInEx will IGNORE this plugin.");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine("Available types in assembly:");
                foreach (var type in types.Take(20))
                {
                    Console.WriteLine($"  - {type.FullName}");
                    var attrs = type.GetCustomAttributes(false);
                    if (attrs.Length > 0)
                    {
                        Console.WriteLine($"    Attributes: {string.Join(", ", attrs.Select(a => a.GetType().Name))}");
                    }
                }
                if (types.Length > 20)
                    Console.WriteLine($"  ... and {types.Length - 20} more types");
                return 1;
            }
            
            // Check dependencies
            Console.WriteLine("Referenced Assemblies:");
            var references = assembly.GetReferencedAssemblies();
            
            var bepinexRefs = references.Where(r => r.Name.Contains("BepInEx")).ToList();
            var melonRefs = references.Where(r => r.Name.Contains("Melon")).ToList();
            var unityRefs = references.Where(r => r.Name.Contains("Unity")).ToList();
            
            Console.WriteLine($"  BepInEx references ({bepinexRefs.Count}):");
            foreach (var refAsm in bepinexRefs)
                Console.WriteLine($"    - {refAsm.Name} (v{refAsm.Version})");
            
            Console.WriteLine($"  MelonLoader references ({melonRefs.Count}):");
            foreach (var refAsm in melonRefs)
                Console.WriteLine($"    - {refAsm.Name} (v{refAsm.Version})");
            
            Console.WriteLine($"  Unity references ({unityRefs.Count}):");
            foreach (var refAsm in unityRefs)
                Console.WriteLine($"    - {refAsm.Name} (v{refAsm.Version})");
            
            var otherRefs = references.Except(bepinexRefs).Except(melonRefs).Except(unityRefs).Take(10).ToList();
            if (otherRefs.Any())
            {
                Console.WriteLine($"  Other references (showing {otherRefs.Count}):");
                foreach (var refAsm in otherRefs)
                    Console.WriteLine($"    - {refAsm.Name} (v{refAsm.Version})");
            }
            
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Plugin validation passed! Found {pluginCount} plugin(s).");
            Console.WriteLine("  This DLL should be recognized by BepInEx.");
            Console.ResetColor();
            
            return 0;
        }
        catch (ReflectionTypeLoadException rtle)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"WARNING: ReflectionTypeLoadException during validation");
            Console.WriteLine($"Successfully loaded {rtle.Types.Count(t => t != null)} types");
            Console.ResetColor();
            
            Console.WriteLine("\nLoader Exceptions:");
            foreach (var loaderEx in rtle.LoaderExceptions.Where(e => e != null).Take(5))
            {
                Console.WriteLine($"  - {loaderEx.Message}");
            }
            
            // Try to check loaded types anyway
            var loadedTypes = rtle.Types.Where(t => t != null).ToArray();
            Console.WriteLine($"\nChecking {loadedTypes.Length} successfully loaded types...");
            
            bool foundPlugin = false;
            foreach (var type in loadedTypes)
            {
                var attributes = type.GetCustomAttributes(false);
                var bepInPluginAttr = attributes.FirstOrDefault(a => 
                    a.GetType().Name == "BepInPlugin" || 
                    a.GetType().Name == "BepInPluginAttribute");
                
                if (bepInPluginAttr != null)
                {
                    foundPlugin = true;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✓ Found BepInPlugin: {type.FullName}");
                    Console.ResetColor();
                }
            }
            
            if (foundPlugin)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n⚠ Plugin attribute found, but there were loading errors.");
                Console.WriteLine("  The plugin may still work if BepInEx can load missing dependencies.");
                Console.ResetColor();
                return 0;
            }
            
            return 1;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.WriteLine($"Type: {ex.GetType().Name}");
            Console.WriteLine($"\nStack Trace:\n{ex.StackTrace}");
            Console.ResetColor();
            return 1;
        }
    }
}
