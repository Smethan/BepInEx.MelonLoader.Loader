#!/usr/bin/env dotnet script

using System;
using System.IO;
using System.Linq;
using System.Reflection;

// Get the DLL path from command line argument
var dllPath = Args.FirstOrDefault() ?? 
    "/Users/smethan/BepInEx.MelonLoader.Loader/BepInEx.MelonLoader.Loader.UnityMono/Output/BepInEx5/UnityMono/BepInEx.MelonLoader.Loader.UnityMono.dll";

Console.WriteLine("BepInEx Plugin Validator");
Console.WriteLine("========================");
Console.WriteLine($"Analyzing: {Path.GetFileName(dllPath)}");
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
    // Load assembly for reflection only
    var assembly = Assembly.LoadFrom(dllPath);
    
    Console.WriteLine($"Assembly: {assembly.FullName}");
    Console.WriteLine();
    
    var types = assembly.GetTypes();
    Console.WriteLine($"Total types found: {types.Length}");
    Console.WriteLine();
    
    // Look for BepInPlugin attributes
    bool foundPlugin = false;
    
    foreach (var type in types)
    {
        var bepInPluginAttr = type.GetCustomAttributes(false)
            .FirstOrDefault(a => a.GetType().Name == "BepInPlugin" || 
                                 a.GetType().Name == "BepInPluginAttribute");
        
        if (bepInPluginAttr != null)
        {
            foundPlugin = true;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Found BepInPlugin: {type.FullName}");
            Console.ResetColor();
            
            // Try to extract GUID, Name, Version
            var attrType = bepInPluginAttr.GetType();
            var guidProp = attrType.GetProperty("GUID") ?? attrType.GetField("GUID");
            var nameProp = attrType.GetProperty("Name") ?? attrType.GetField("Name");
            var versionProp = attrType.GetProperty("Version") ?? attrType.GetField("Version");
            
            if (guidProp != null)
            {
                var guid = guidProp is PropertyInfo pi ? pi.GetValue(bepInPluginAttr) : 
                          ((FieldInfo)guidProp).GetValue(bepInPluginAttr);
                Console.WriteLine($"  GUID: {guid}");
            }
            
            if (nameProp != null)
            {
                var name = nameProp is PropertyInfo pi ? pi.GetValue(bepInPluginAttr) : 
                          ((FieldInfo)nameProp).GetValue(bepInPluginAttr);
                Console.WriteLine($"  Name: {name}");
            }
            
            if (versionProp != null)
            {
                var version = versionProp is PropertyInfo pi ? pi.GetValue(bepInPluginAttr) : 
                             ((FieldInfo)versionProp).GetValue(bepInPluginAttr);
                Console.WriteLine($"  Version: {version}");
            }
            
            // Check base class
            Console.WriteLine($"  Base Type: {type.BaseType?.Name ?? "None"}");
            
            // Check for required methods
            var awakeMethod = type.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            var loadMethod = type.GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            
            if (awakeMethod != null)
                Console.WriteLine($"  ✓ Has Awake() method");
            if (loadMethod != null)
                Console.WriteLine($"  ✓ Has Load() method");
            
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
        Console.WriteLine("Available types:");
        foreach (var type in types.Take(10))
        {
            Console.WriteLine($"  - {type.FullName}");
        }
        return 1;
    }
    
    // Check dependencies
    Console.WriteLine("Dependencies:");
    var references = assembly.GetReferencedAssemblies();
    foreach (var refAsm in references.Take(15))
    {
        Console.WriteLine($"  - {refAsm.Name} ({refAsm.Version})");
    }
    
    if (references.Length > 15)
        Console.WriteLine($"  ... and {references.Length - 15} more");
    
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("✓ Plugin validation passed!");
    Console.WriteLine("  This DLL should be recognized by BepInEx.");
    Console.ResetColor();
    
    return 0;
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"ERROR: {ex.Message}");
    Console.WriteLine($"Type: {ex.GetType().Name}");
    Console.ResetColor();
    
    if (ex is ReflectionTypeLoadException rtle)
    {
        Console.WriteLine("\nLoader Exceptions:");
        foreach (var loaderEx in rtle.LoaderExceptions.Take(5))
        {
            Console.WriteLine($"  - {loaderEx?.Message}");
        }
    }
    
    return 1;
}
