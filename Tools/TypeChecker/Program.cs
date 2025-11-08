using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace TypeChecker;

class Program
{
    static void Main(string[] args)
    {
        // Path to MelonLoader's Assembly-CSharp.dll
        var assemblyPath = args.Length > 0
            ? args[0]
            : "/mnt/c/Users/Ethan/AppData/Roaming/r2modmanPlus-local/Megabonk/profiles/Default/BepInEx/interop/Assembly-CSharp.dll";

        if (!File.Exists(assemblyPath))
        {
            Console.WriteLine($"ERROR: Assembly not found at {assemblyPath}");
            return;
        }

        Console.WriteLine($"Reading assembly metadata from: {assemblyPath}");
        Console.WriteLine();

        try
        {
            // Types we're looking for (from BepInEx mod errors)
            var requiredTypes = new[]
            {
                "ItemData",
                "PlayerInput",
                "EItemRarity",
                "ChestWindowUi"
            };

            Console.WriteLine("Checking for required BepInEx mod types:");
            Console.WriteLine("===========================================");
            Console.WriteLine();

            // Read metadata without loading types (avoids dependency issues)
            using var fileStream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(fileStream);
            var metadataReader = peReader.GetMetadataReader();

            var allTypeNames = new List<string>();

            foreach (var typeDefHandle in metadataReader.TypeDefinitions)
            {
                var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
                var typeName = metadataReader.GetString(typeDef.Name);
                var typeNamespace = typeDef.Namespace.IsNil ? "" : metadataReader.GetString(typeDef.Namespace);
                var fullName = string.IsNullOrEmpty(typeNamespace) ? typeName : $"{typeNamespace}.{typeName}";
                allTypeNames.Add(fullName);
            }

            Console.WriteLine($"✓ Successfully read {allTypeNames.Count} type definitions from metadata");
            Console.WriteLine();

            var foundTypes = new List<string>();
            var missingTypes = new List<string>();

            foreach (var typeName in requiredTypes)
            {
                // Try exact name match first (just the type name, not full namespace)
                var exactMatch = allTypeNames.FirstOrDefault(t =>
                    t.Split('.').Last().Equals(typeName, StringComparison.Ordinal));

                if (exactMatch != null)
                {
                    foundTypes.Add(typeName);
                    Console.WriteLine($"✓ FOUND: {typeName}");
                    Console.WriteLine($"    Full name: {exactMatch}");
                }
                else
                {
                    // Try partial match
                    var partialMatches = allTypeNames
                        .Where(t => t.Contains(typeName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (partialMatches.Count > 0)
                    {
                        foundTypes.Add(typeName);
                        Console.WriteLine($"? PARTIAL MATCHES for '{typeName}':");
                        foreach (var match in partialMatches.Take(5))
                        {
                            Console.WriteLine($"    - {match}");
                        }
                        if (partialMatches.Count > 5)
                        {
                            Console.WriteLine($"    ... and {partialMatches.Count - 5} more");
                        }
                    }
                    else
                    {
                        missingTypes.Add(typeName);
                        Console.WriteLine($"✗ MISSING: {typeName} (no exact or partial match)");
                    }
                }
                Console.WriteLine();
            }

            Console.WriteLine("Summary:");
            Console.WriteLine($"  Found (exact or partial): {foundTypes.Count}/{requiredTypes.Length}");
            Console.WriteLine($"  Missing: {missingTypes.Count}/{requiredTypes.Length}");
            Console.WriteLine();

            if (missingTypes.Count == 0)
            {
                Console.WriteLine("✓ All required types are present in MelonLoader's assemblies!");
                Console.WriteLine("  → BepInEx mods should work with MelonLoader assemblies");
            }
            else
            {
                Console.WriteLine("✗ Some types are completely missing:");
                foreach (var missing in missingTypes)
                {
                    Console.WriteLine($"  - {missing}");
                }
                Console.WriteLine();
                Console.WriteLine("  → BepInEx mods may fail with TypeLoadException");
                Console.WriteLine("  → The frameworks may be incompatible");
            }

            // Show statistics
            Console.WriteLine();
            Console.WriteLine($"Total type definitions in assembly: {allTypeNames.Count}");

            // Show a sample of types
            Console.WriteLine();
            Console.WriteLine("Sample of types in assembly (first 30):");
            foreach (var type in allTypeNames.Take(30))
            {
                Console.WriteLine($"  - {type}");
            }

            if (allTypeNames.Count > 30)
            {
                Console.WriteLine($"  ... and {allTypeNames.Count - 30} more");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine();
            Console.WriteLine(ex.StackTrace);
        }
    }
}
