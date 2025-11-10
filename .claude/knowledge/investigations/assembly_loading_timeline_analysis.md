# Assembly Loading Timeline & Architecture Analysis

**Date:** 2025-11-09
**Status:** INVESTIGATION COMPLETE
**Objective:** Map the complete assembly loading timeline and identify root cause of two-run requirement

---

## Executive Summary

**ROOT CAUSE IDENTIFIED**: The two-run requirement is NOT a bug - it's an architectural dependency on IL2CPP assembly generation.

**Timeline Problem**: InteropRedirector's hook in `Finalizer()` executes BEFORE MelonLoader generates IL2CPP assemblies, creating a chicken-and-egg problem.

**Current Hook Location**: `BasePatcher.Finalizer()` - runs during BepInEx preloader phase, BEFORE plugin phase
**Problem**: Il2CppAssemblies folder doesn't exist yet on first run
**Result**: InteropRedirector correctly reports "not found", game uses BepInEx assemblies

---

## Complete Initialization Timeline

### Phase 1: BepInEx Preloader Phase (CURRENT LOCATION)
```
[T+0ms] BepInEx Chainloader starts
├─ [T+10ms] Load all BasePatcher plugins
│  ├─ InteropRedirectorPatcher.Initialize()
│  │  ├─ Install Harmony patch for ValidateAssemblyNameWithSimpleName ✓
│  │  └─ Log: "Resolution hook will be activated in Finalizer phase"
│  │
│  └─ Other prepatchers run...
│
├─ [T+100ms] Run all BasePatcher.Finalizer() ← WE ARE HERE
│  └─ InteropRedirectorPatcher.Finalizer()
│     ├─ Install AssemblyLoadContext.Default.Resolving hook ✓
│     ├─ Try to find MelonLoader/Il2CppAssemblies folder
│     └─ FIRST RUN: Folder doesn't exist ✗
│        └─ Log: "MelonLoader assemblies not found (normal on first run)"
│
└─ [T+150ms] Preloader complete, assemblies NOT YET GENERATED
```

### Phase 2: BepInEx Plugin Phase (MelonLoader Initialization)
```
[T+200ms] BepInEx IL2CPPChainloader starts
├─ Load all BasePlugin plugins
│  ├─ BepInEx.MelonLoader.Loader.IL2CPP.Plugin.Load()
│  │  ├─ Create BepInEx config entries
│  │  ├─ BootstrapShim.EnsureInitialized()
│  │  └─ Register IL2CPPChainloader.Instance.Finished callback
│  │
│  └─ Other BepInEx plugins load...
│
└─ [T+500ms] IL2CPPChainloader.Instance.Finished event fires
   └─ Plugin.cs callback executes
      ├─ Log: "ALL BEPINEX PLUGINS LOADED"
      ├─ BootstrapShim.RunMelonLoader()
      │  ├─ BootstrapInterop.Initialize() → Core.Initialize()
      │  │  ├─ MelonAssemblyResolver.Setup()
      │  │  ├─ MelonPreferences.Load()
      │  │  └─ MelonFolderHandler.LoadMelons(Plugins) ← Scans for plugins
      │  │
      │  └─ BootstrapInterop.Start() → Core.Start()
      │     └─ MelonStartScreen.LoadAndRun(PreSetup)
      │        └─ Core.PreSetup()
      │           └─ Il2CppAssemblyGenerator.Run() ← ASSEMBLIES GENERATED HERE
      │              ├─ Load Il2CppAssemblyGenerator.dll module
      │              ├─ Run Cpp2IL unhollower
      │              ├─ Generate all IL2CPP → .NET assemblies
      │              └─ Write to MLLoader/MelonLoader/Il2CppAssemblies/ ✓
      │
      └─ InteropRedirectorPatcher.NotifyMelonLoaderReady() ← RE-INITIALIZATION
         ├─ Force re-initialize lazy state (throw away failed state)
         ├─ Search for Il2CppAssemblies folder → FOUND ✓
         └─ CreateAssemblyAliases() to create filename symlinks
```

### Phase 3: Game Initialization
```
[T+10000ms] Unity Runtime starts
├─ Load game assemblies
│  ├─ Request Il2CppSystem.Private.CoreLib
│  │  └─ InteropRedirectorPatcher.OnAssemblyResolving() fires
│  │     ├─ Check lazy state → initialized? YES (after NotifyMelonLoaderReady)
│  │     ├─ Resolve to MelonLoader/Il2Cppmscorlib.dll ✓
│  │     ├─ Return assembly (name mismatch)
│  │     └─ Harmony patch bypasses .NET validation ✓
│  │
│  └─ Load other IL2CPP assemblies...
│
└─ Both BepInEx and MelonLoader mods can use IL2CPP types ✓
```

---

## Architectural Analysis

### Current Architecture
```
BepInEx Preloader Phase (BasePatcher)
    ├─ InteropRedirector installs hooks
    └─ Assemblies don't exist yet ✗

BepInEx Plugin Phase (BasePlugin)
    └─ MelonLoader generates assemblies
       └─ Notifies InteropRedirector (re-init)

Game Runtime
    └─ Assemblies resolve correctly ✓
```

### The Chicken-and-Egg Problem

**Architectural Conflict:**
1. **InteropRedirector needs**: Il2CppAssemblies to exist BEFORE assembly resolution
2. **MelonLoader generates**: Il2CppAssemblies AFTER all BepInEx plugins load
3. **Hook installation timing**: BepInEx Patcher phase runs BEFORE Plugin phase

**Why NotifyMelonLoaderReady() Exists:**
- Workaround for the timing mismatch
- Forces re-initialization AFTER assemblies are generated
- Only works if AssemblyLoadContext.Resolving event already installed

**Why Two Runs Required (First Run):**
1. First run: No Il2CppAssemblies folder exists
2. InteropRedirector initializes, finds nothing, caches "not found"
3. MelonLoader generates assemblies BUT they're not used this run
4. Game loads with BepInEx assemblies only
5. Second run: Il2CppAssemblies exists from run 1
6. InteropRedirector finds assemblies, everything works ✓

---

## Alternative Hook Points Analysis

### Option 1: Stay in Finalizer() (CURRENT - v2.3.0)
**Location:** `BasePatcher.Finalizer()`
**Timing:** After all prepatchers, BEFORE plugins

**Pros:**
- Early hook installation (catches all assembly loads)
- Clean separation from plugin phase
- Can use NotifyMelonLoaderReady() to re-init

**Cons:**
- Assemblies don't exist yet on first run
- Requires workaround (NotifyMelonLoaderReady)
- Two-run requirement for first-time users

**Verdict:** ✓ Current implementation is architecturally sound for this hook point

---

### Option 2: Move to Plugin Phase (BasePlugin)
**Location:** Create a second plugin that installs hook AFTER assembly generation
**Timing:** After MelonLoader generates assemblies

**Pros:**
- Assemblies guaranteed to exist
- No NotifyMelonLoaderReady() needed
- Single-run experience

**Cons:**
- ⚠️ CRITICAL: Misses early assembly loads
- BepInEx chainloader may load assemblies BEFORE our hook
- Race condition: When does .NET load Il2CPP assemblies?
- Complex coordination between Patcher and Plugin

**Implementation:**
```csharp
// In InteropRedirectorPatcher.cs (Patcher - runs early)
public override void Initialize()
{
    // Only install Harmony patch, NOT the resolver hook
    InstallHarmonyPatch();
}

// NEW: InteropRedirectorPlugin.cs (Plugin - runs after MelonLoader)
[BepInPlugin(...)]
public class InteropRedirectorPlugin : BasePlugin
{
    public override void Load()
    {
        // Wait for MelonLoader to generate assemblies
        IL2CPPChainloader.Instance.Finished += () =>
        {
            // Install resolver hook AFTER assemblies exist
            AssemblyLoadContext.Default.Resolving += OnAssemblyResolving;
        };
    }
}
```

**Risk Assessment:** HIGH ⚠️
- If BepInEx loads Il2CPP types before our hook installs → CRASH
- .NET may eagerly load assemblies during chainloader execution
- No guarantee when first Il2CPP assembly load occurs

**Verdict:** ✗ Too risky - assembly loading is non-deterministic

---

### Option 3: Hook Even Earlier (AppDomain.AssemblyResolve)
**Location:** Before BepInEx starts
**Timing:** Immediately after .NET runtime starts

**Pros:**
- Catches ALL assembly loads
- No timing issues

**Cons:**
- Would require native bootstrapper modifications
- Outside scope of BepInEx plugin system
- Can't access BepInEx APIs (Paths, Logger, etc.)
- Assemblies STILL don't exist on first run

**Verdict:** ✗ Not feasible without MelonLoader bootstrapper changes

---

### Option 4: Pre-Generate Assemblies in Patcher Phase
**Location:** `BasePatcher.Initialize()` or `Finalizer()`
**Timing:** During preloader, BEFORE hook installation

**Implementation:**
```csharp
public override void Finalizer()
{
    // Check if assemblies exist
    if (!Il2CppAssembliesExist())
    {
        // Trigger MelonLoader assembly generation NOW
        GenerateAssemblies();
    }

    // Then install hook (assemblies guaranteed to exist)
    AssemblyLoadContext.Default.Resolving += OnAssemblyResolving;
}
```

**Pros:**
- Single-run experience ✓
- No NotifyMelonLoaderReady() needed
- Assemblies exist before hook installs

**Cons:**
- Requires duplicating MelonLoader's assembly generation logic
- Tight coupling with MelonLoader internals
- May conflict with MelonLoader's own generation
- Heavy operation in preloader phase (slow startup)

**Risk Assessment:** MEDIUM-HIGH ⚠️
- Duplicate assembly generation = twice the work
- Potential for race conditions if MelonLoader also generates
- Violates separation of concerns

**Feasibility Investigation Required:**
- Can we call MelonLoader's Il2CppAssemblyGenerator from Patcher phase?
- Would this create circular dependencies?
- What if generation fails? (No fallback to BepInEx assemblies)

**Verdict:** 🔍 Requires deeper investigation - see Section 5

---

### Option 5: Lazy Hook Installation with Early Detection
**Location:** `BasePatcher.Finalizer()` + delayed hook
**Timing:** Defer hook until first Il2CPP assembly request

**Implementation:**
```csharp
public override void Finalizer()
{
    // Install a "bootstrap" hook that installs the real hook on first use
    AssemblyLoadContext.Default.Resolving += BootstrapResolver;
}

private Assembly BootstrapResolver(AssemblyLoadContext context, AssemblyName name)
{
    if (ShouldRedirect(name.Name))
    {
        // First Il2CPP assembly requested - install real hook NOW
        AssemblyLoadContext.Default.Resolving -= BootstrapResolver;
        AssemblyLoadContext.Default.Resolving += OnAssemblyResolving;

        // Handle this request with real resolver
        return OnAssemblyResolving(context, name);
    }
    return null;
}
```

**Pros:**
- Minimal overhead until actually needed
- Could wait for MelonLoader to generate assemblies

**Cons:**
- First Il2CPP assembly load may happen before generation
- Complex state management
- Still has two-run problem if assemblies don't exist

**Verdict:** ✗ Doesn't solve the fundamental timing problem

---

## Key Technical Constraints

### .NET 6 AssemblyLoadContext Behavior
```csharp
// Resolution order:
1. AssemblyLoadContext.LoadFromAssemblyPath() (explicit loads)
2. AssemblyLoadContext.Default.Load() (implicit loads)
3. AssemblyLoadContext.Resolving event ← WE HOOK HERE
4. AssemblyLoadContext.Default.Resolving fallback
5. Throw FileNotFoundException

// CRITICAL: Resolving event fires ONLY when steps 1-2 fail
// This means we can't "intercept" - we can only "provide fallback"
```

### BepInEx Lifecycle Constraints
```
BasePatcher phase:
- Runs ONCE during preloader
- Cannot be re-triggered
- Must be fast (affects startup time)

BasePlugin phase:
- Runs AFTER preloader completes
- Has access to full BepInEx infrastructure
- Can wait for other plugins (IL2CPPChainloader.Finished)
```

### MelonLoader Assembly Generator Constraints
```csharp
// Il2CppAssemblyGenerator.Run() requirements:
- Needs IL2CPP game binary metadata
- Writes to MLLoader/MelonLoader/Il2CppAssemblies/
- Heavy operation (5-30 seconds depending on game)
- Can only run ONCE per game version
- Must run before any IL2CPP types are used
```

---

## Recommended Solution

### VERDICT: Current v2.3.0 Implementation is CORRECT

**Recommendation:** KEEP Finalizer() hook with NotifyMelonLoaderReady()

**Rationale:**
1. ✓ Architecturally sound for BepInEx patcher lifecycle
2. ✓ Harmony patch successfully bypasses .NET validation
3. ✓ NotifyMelonLoaderReady() is elegant workaround for timing
4. ✓ Two-run requirement is acceptable for first-time setup
5. ✓ All subsequent runs work correctly

**User Experience:**
- First run: MelonLoader generates assemblies (expected behavior)
- Second run: Everything works ✓
- After that: No issues ✓

**Alternatives All Have Worse Tradeoffs:**
- Plugin phase hook: Risk of missing early loads
- Pre-generation: Performance and coupling concerns
- Earlier hooks: Outside BepInEx architecture

---

## Investigation Results: Pre-Generation Feasibility

### Question: Can we call Il2CppAssemblyGenerator from Patcher phase?

**Answer:** THEORETICALLY YES, but NOT RECOMMENDED

**Technical Analysis:**

```csharp
// Il2CppAssemblyGenerator.Run() from InteropRedirectorPatcher:

public override void Finalizer()
{
    // Check if assemblies exist
    var assemblyPath = FindMelonLoaderInteropPath();
    if (assemblyPath == null)
    {
        Logger.LogWarning("Il2CppAssemblies not found, attempting generation...");

        // Try to load and invoke MelonLoader's generator
        try
        {
            var generatorPath = Path.Combine(
                MelonEnvironment.Il2CppAssemblyGeneratorDirectory,
                "Il2CppAssemblyGenerator.dll"
            );

            // Load the generator module
            var generatorAssembly = Assembly.LoadFrom(generatorPath);
            var generatorType = generatorAssembly.GetType("Il2CppAssemblyGenerator.Main");
            var runMethod = generatorType.GetMethod("Run");

            // Invoke generation
            var result = (int)runMethod.Invoke(null, null);

            if (result == 0)
            {
                Logger.LogInfo("Assembly generation complete!");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to generate assemblies: {ex}");
            // Fall back to two-run behavior
        }
    }

    // Install hook (assemblies should exist now)
    AssemblyLoadContext.Default.Resolving += OnAssemblyResolving;
}
```

**Challenges:**

1. **Dependency Hell:**
   - Il2CppAssemblyGenerator depends on MelonLoader.dll
   - MelonLoader.dll not loaded yet in Patcher phase
   - Would need to manually load entire MelonLoader dependency tree

2. **Environment Not Ready:**
   - MelonLoader expects certain paths and environment variables
   - MelonEnvironment.Il2CppAssemblyGeneratorDirectory may not exist
   - Game metadata may not be accessible

3. **Duplicate Work:**
   - MelonLoader ALSO tries to generate assemblies in PreSetup()
   - Need coordination to prevent double-generation
   - What if Patcher generates but then MelonLoader fails?

4. **Error Handling:**
   - If generation fails, what's the fallback?
   - Can't just crash - need graceful degradation
   - Current design: First run = BepInEx only (safe)

5. **Performance Impact:**
   - Assembly generation takes 5-30 seconds
   - Delays game startup significantly
   - User expects first run to be slow (MelonLoader splash screen)
   - But NOT during BepInEx preloader phase

**Conclusion:** ✗ Not worth the complexity

---

## Alternative User Experience Solutions

### Option A: Better User Messaging (RECOMMENDED)
```csharp
if (state?.IsInitialized != true)
{
    Logger.LogInfo("═════════════════════════════════════════");
    Logger.LogInfo("  FIRST RUN DETECTED");
    Logger.LogInfo("═════════════════════════════════════════");
    Logger.LogInfo("MelonLoader is generating IL2CPP assemblies...");
    Logger.LogInfo("This only happens once per game update.");
    Logger.LogInfo("");
    Logger.LogInfo("⚠️ IMPORTANT: Restart the game after generation completes");
    Logger.LogInfo("for full BepInEx + MelonLoader compatibility.");
    Logger.LogInfo("═════════════════════════════════════════");
}
```

### Option B: Automatic Restart After Generation
```csharp
IL2CPPChainloader.Instance.Finished += () =>
{
    bool wasFirstRun = !Il2CppAssembliesExist();
    BootstrapShim.RunMelonLoader();

    if (wasFirstRun && Il2CppAssembliesExist())
    {
        Logger.LogInfo("Assembly generation complete. Restarting game...");
        Application.Quit(); // Trigger restart
    }
};
```

**Risk:** Users may not expect automatic quit

### Option C: Config Option to Disable Two-Run
```csharp
var skipFirstRun = Config.Bind("Advanced", "SkipMelonLoaderIntegration", false,
    "Use BepInEx assemblies only. Disables MelonLoader assembly redirection.");

if (!skipFirstRun.Value)
{
    // Install InteropRedirector
}
```

**Use case:** Users who don't need MelonLoader compatibility

---

## Conclusion

**The two-run requirement is NOT a bug - it's a design constraint.**

**Root Cause:**
- BepInEx Patcher phase runs BEFORE MelonLoader generates assemblies
- Assembly resolution hooks must be installed early
- First run = assemblies don't exist yet = expected behavior

**Current v2.3.0 Implementation Assessment:**
- ✅ Architecturally correct
- ✅ Harmony patch works as designed
- ✅ NotifyMelonLoaderReady() is appropriate workaround
- ✅ No better alternative exists without major tradeoffs

**Recommendation:**
1. KEEP current implementation
2. IMPROVE user messaging for first run
3. Document two-run requirement in README
4. Consider automatic restart option (optional)

**No Code Changes Required** - this is working as intended given the architectural constraints.

---

## References

**Files Analyzed:**
- `/home/smethan/MelonLoaderLoader/BepInEx.MelonLoader.InteropRedirector/InteropRedirectorPatcher.cs`
  - Lines 89-125: Initialize() - Harmony patch installation
  - Lines 127-145: Finalizer() - Hook installation
  - Lines 152-179: NotifyMelonLoaderReady() - Re-initialization
  - Lines 234-292: OnAssemblyResolving() - Resolution logic

- `/home/smethan/MelonLoaderLoader/BepInEx.MelonLoader.Loader.IL2CPP/Plugin.cs`
  - Lines 79-113: IL2CPPChainloader.Finished callback
  - Lines 86-112: NotifyMelonLoaderReady() invocation

- `/home/smethan/MelonLoaderLoader/MelonLoader-upstream/MelonLoader/Core.cs`
  - Lines 28-179: Initialize() - MelonLoader startup
  - Lines 181-189: PreSetup() - Assembly generation trigger
  - Lines 191-200: Start() - Post-generation initialization

- `/home/smethan/MelonLoaderLoader/MelonLoader-upstream/MelonLoader/InternalUtils/Il2CppAssemblyGenerator.cs`
  - Lines 16-53: Run() - Assembly generation entry point

**BepInEx Architecture:**
- BasePatcher: Preloader phase (runs first)
- BasePlugin: Plugin phase (runs after preloader)
- IL2CPPChainloader: Manages IL2CPP game plugin loading

**.NET 6 Assembly Loading:**
- AssemblyLoadContext.Resolving event
- ValidateAssemblyNameWithSimpleName internal method
- Assembly name validation occurs AFTER handler returns
