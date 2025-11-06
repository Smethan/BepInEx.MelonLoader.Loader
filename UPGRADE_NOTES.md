# MelonLoader 0.5.7 → 0.7.1 Upgrade Notes

## Status

The project is **partially updated** to MelonLoader 0.7.1. The following work has been completed:

### ✅ Completed Updates

1. **Version Strings Updated**
   - [BuildInfo.cs](MelonLoader/Properties/BuildInfo.cs:9) - Updated version to "0.7.1"
   - [README.md](README.md:7) - Updated documentation to reflect 0.7.1 support

2. **Namespace Fixes**
   - Fixed `ColorARGB` namespace from `MelonLoader.Bootstrap.Logging` to `MelonLoader.Logging` ([BootstrapShim.cs:12](Shared/BootstrapShim.cs#L12))

3. **LoaderConfig Compatibility**
   - Added reflection-based property setter for internal properties ([BootstrapShim.cs:44-50](Shared/BootstrapShim.cs#L44-L50))
   - Updated all LoaderConfig property assignments to use reflection

4. **.NET 3.5 Compatibility Helpers**
   - Added `IsNullOrWhiteSpace` helper method ([BootstrapShim.cs:38-41](Shared/BootstrapShim.cs#L38-L41))
   - Replaced all `string.IsNullOrWhiteSpace` calls with custom implementation

## ⚠️ Critical Issues Remaining

### .NET 3.5 Compatibility Problems

The `BootstrapShim.cs` file was written for a modern .NET version but needs to compile for .NET 3.5 (Unity Mono). The following language features and APIs don't exist in .NET 3.5:

#### Type System Issues
- **`nint` type** - Replace with `IntPtr` (40+ occurrences)
  - `nint.Zero` → `IntPtr.Zero`
  - All `nint` declarations → `IntPtr`

#### Framework API Issues
- **`AppContext.BaseDirectory`** ([line 157](Shared/BootstrapShim.cs#L157)) - Doesn't exist in .NET 3.5
  - Replace with `AppDomain.CurrentDomain.BaseDirectory`

- **`Path.Combine` with 3+ arguments** (lines 173, 174, 396, 454) - Only supports 2 args in .NET 3.5
  - Nest calls: `Path.Combine(Path.Combine(a, b), c)`

- **`Enum.TryParse<T>()`** ([line 552](Shared/BootstrapShim.cs#L552)) - Doesn't exist in .NET 3.5
  - Replace with `Enum.Parse()` wrapped in try-catch

- **`PropertyInfo.SetValue()` with 2 arguments** (lines 82, 110) - Requires 3 args in .NET 3.5
  - Add `null` as third parameter for indexer

#### Namespace Resolution Issues
Many types are being looked for in `BepInEx.MelonLoader` namespace but should be in `MelonLoader`:
- `MelonLoader.MelonLaunchOptions` (not `BepInEx.MelonLoader.MelonLaunchOptions`)
- `MelonLoader.Utils.MelonEnvironment`
- `MelonLoader.NativeLibrary`
- `MelonLoader.Core`

This appears to be a compilation issue where the namespace inference is incorrect.

### LoaderConfig.Current Assignment
- **Line 442**: Attempts to set `LoaderConfig.Current` which has `internal set`
  - This may need to be done via reflection as well, or removed entirely

## Key API Changes in MelonLoader 0.7.1

### 1. Logging System
- `ColorARGB` moved from `MelonLoader.Bootstrap.Logging` → `MelonLoader.Logging`
- Structure is now in ColorRGB.cs file
- Implements full ARGB color support with named constants

### 2. LoaderConfig System
- All config properties now use `{ get; internal set; }` pattern
- Properties can only be set from within MelonLoader assembly
- External code must use reflection to modify properties
- Config loaded from `UserData/Loader.cfg` (TOML format)
- Five configuration sections:
  - `CoreConfig` (Loader)
  - `ConsoleConfig` (Console)
  - `LogsConfig` (Logs)
  - `MonoDebugServerConfig` (MonoDebugServer)
  - `UnityEngineConfig` (UnityEngine)

### 3. Assembly Resolution
- MelonLoader 0.7.1 redesigned assembly resolver with separate Search/Resolve functions
- Support Module Component Registration was reimplemented
- May affect `MonoInternals/ResolveInternals` integration

### 4. Removed Features
- `manifest.json` requirement removed for recursive folder scanning
- SharpZipLib and TinyJSON moved to BackwardsCompatibility namespace
- Obsolete members converted to compile errors

## Recommended Fix Strategy

### Option 1: Conditional Compilation (Recommended)
Create separate code paths for .NET 3.5 (Unity Mono) and modern .NET (IL2CPP):

```csharp
#if NET35
    private static IntPtr _monoRuntimeHandle;  // Use IntPtr
#else
    private static nint _monoRuntimeHandle;    // Use nint
#endif
```

### Option 2: Modernize Build Target
Consider updating Unity Mono builds to target .NET 4.x if the target games support it. This would allow using modern C# features.

### Option 3: Backport All Code
Systematically replace all modern features with .NET 3.5 equivalents. This is labor-intensive but ensures maximum compatibility.

## Testing Checklist

Once compilation issues are resolved, test:

- [ ] Bootstrap initialization and delegate binding
- [ ] LoaderConfig loading from TOML file
- [ ] Launch option override application via reflection
- [ ] Mono runtime hooks and assembly resolution
- [ ] Native hook attach/detach functionality
- [ ] BepInEx logging integration
- [ ] Both Unity Mono (BepInEx 5/6) and IL2CPP (BepInEx 6) builds
- [ ] Sample MelonLoader mods loading correctly
- [ ] Configuration persistence to `UserData/Loader.cfg`

## Dependencies

Current dependency versions:
- **BepInEx 5**: 5.4.21 (Unity Mono)
- **BepInEx 6**: 6.0.0-be.572 (Unity Mono & IL2CPP)
- **MelonLoader**: 0.7.1
- **MonoMod.RuntimeDetour**: 22.7.31.1
- **Tomlet**: 6.0.0 (Loaders) / 5.0.0 (MelonLoader)
- **HarmonyX**: 2.10.0

### Tomlet Version Discrepancy
There's a version mismatch between:
- Loader plugins: Tomlet 6.0.0
- MelonLoader assembly: Tomlet 5.0.0

This could cause TOML serialization compatibility issues. Monitor for deserialization errors.

## References

- [MelonLoader v0.7.1 Release](https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.1)
- [MelonLoader Changelog](https://github.com/LavaGang/MelonLoader/blob/master/CHANGELOG.md)
- [MelonLoader 0.6.0-0.7.1 API Changes](docs/CHANGELOG_SUMMARY.md)

## Next Steps

1. Fix .NET 3.5 compatibility issues in [BootstrapShim.cs](Shared/BootstrapShim.cs)
2. Resolve namespace resolution issues
3. Build and test with sample MelonLoader mods
4. Verify delegate binding works with 0.7.1 internals
5. Test both BepInEx 5 and BepInEx 6 configurations
6. Update build scripts if needed
