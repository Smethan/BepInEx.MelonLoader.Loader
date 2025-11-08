using Mono.Cecil;
using Mono.Cecil.Cil;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: PatchAssemblyGenerator <input-dll> <output-dll>");
            Console.WriteLine("Patches Il2CppAssemblyGenerator.dll to change Il2CppPrefixMode from OptOut to OptIn");
            return;
        }

        var inputPath = args[0];
        var outputPath = args[1];

        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"Error: Input file not found: {inputPath}");
            return;
        }

        Console.WriteLine($"Loading assembly: {inputPath}");
        var assembly = AssemblyDefinition.ReadAssembly(inputPath);

        // Find the Il2CppInterop class
        var il2cppInteropType = assembly.MainModule.Types
            .FirstOrDefault(t => t.FullName == "MelonLoader.Il2CppAssemblyGenerator.Packages.Il2CppInterop");

        if (il2cppInteropType == null)
        {
            Console.WriteLine("Error: Could not find Il2CppInterop class");
            return;
        }

        Console.WriteLine("✓ Found Il2CppInterop class");

        // Find the Execute method
        var executeMethod = il2cppInteropType.Methods
            .FirstOrDefault(m => m.Name == "Execute");

        if (executeMethod == null)
        {
            Console.WriteLine("Error: Could not find Execute method");
            return;
        }

        Console.WriteLine("✓ Found Execute method");

        // Look for the instruction that sets Il2CppPrefixMode
        // We're looking for: ldc.i4.1 (OptOut = 1) and changing it to ldc.i4.0 (OptIn = 0)
        bool patched = false;
        var processor = executeMethod.Body.GetILProcessor();

        for (int i = 0; i < executeMethod.Body.Instructions.Count; i++)
        {
            var instruction = executeMethod.Body.Instructions[i];

            // Look for the pattern where Il2CppPrefixMode is set
            // This should be: ldc.i4.1 followed by setting the property
            if (instruction.OpCode == OpCodes.Ldc_I4_1 && i + 1 < executeMethod.Body.Instructions.Count)
            {
                var nextInstruction = executeMethod.Body.Instructions[i + 1];

                // Check if the next instruction sets a property related to PrefixMode
                if (nextInstruction.OpCode == OpCodes.Callvirt ||
                    nextInstruction.OpCode == OpCodes.Stfld ||
                    nextInstruction.OpCode == OpCodes.Call)
                {
                    var operand = nextInstruction.Operand?.ToString() ?? "";
                    if (operand.Contains("Il2CppPrefixMode") || operand.Contains("PrefixMode"))
                    {
                        Console.WriteLine($"✓ Found Il2CppPrefixMode assignment at instruction {i}");
                        Console.WriteLine($"  Current value: OptOut (1)");

                        // Change ldc.i4.1 to ldc.i4.0 (OptOut to OptIn)
                        var newInstruction = processor.Create(OpCodes.Ldc_I4_0);
                        processor.Replace(instruction, newInstruction);

                        Console.WriteLine($"  Patched to: OptIn (0)");
                        patched = true;
                        break;
                    }
                }
            }
        }

        if (!patched)
        {
            Console.WriteLine("Warning: Could not find Il2CppPrefixMode assignment to patch");
            Console.WriteLine("The DLL structure may have changed. Manual inspection required.");
        }
        else
        {
            Console.WriteLine($"✓ Writing patched assembly to: {outputPath}");
            assembly.Write(outputPath);
            Console.WriteLine("✓ Patching complete!");
            Console.WriteLine();
            Console.WriteLine("Summary:");
            Console.WriteLine("  Changed Il2CppPrefixMode from OptOut to OptIn");
            Console.WriteLine("  This will generate assemblies WITHOUT the Il2Cpp namespace prefix");
            Console.WriteLine("  Making them compatible with BepInEx mods");
        }
    }
}
