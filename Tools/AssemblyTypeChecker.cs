using System;
using System.IO;
using System.Linq;
using System.Reflection;

class AssemblyTypeChecker
{
    static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: AssemblyTypeChecker <path-to-Assembly-CSharp.dll> [type-to-search]");
            return;
        }

        var assemblyPath = args[0];
        if (!File.Exists(assemblyPath))
        {
            Console.WriteLine($"Assembly not found: {assemblyPath}");
            return;
        }

        try
        {
            var assembly = Assembly.LoadFrom(assemblyPath);
            Console.WriteLine($"Loaded: {assembly.FullName}");
            Console.WriteLine($"Assembly size: {new FileInfo(assemblyPath).Length:N0} bytes");
            Console.WriteLine();

            var types = assembly.GetTypes();
            Console.WriteLine($"Total types: {types.Length:N0}");
            Console.WriteLine();

            // Check for specific types
            var typesToFind = new[] { "ItemData", "PlayerInput", "EItemRarity", "ChestWindowUi" };

            if (args.Length > 1)
            {
                typesToFind = args.Skip(1).ToArray();
            }

            Console.WriteLine("Searching for types:");
            foreach (var typeName in typesToFind)
            {
                var found = types.Where(t => t.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase)).ToList();

                if (found.Any())
                {
                    Console.WriteLine($"✓ {typeName}:");
                    foreach (var t in found)
                    {
                        Console.WriteLine($"  - {t.FullName}");
                    }
                }
                else
                {
                    Console.WriteLine($"✗ {typeName}: NOT FOUND");
                }
            }

            Console.WriteLine();
            Console.WriteLine("Sample of available types:");
            foreach (var t in types.Take(20))
            {
                Console.WriteLine($"  {t.FullName}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
