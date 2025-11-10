# BepInEx Patcher Lifecycle Timing Pattern
**Pattern Type**: Initialization Timing
**Framework**: BepInEx 6.x Preloader
**Language**: C# / .NET 6+
**Last Updated**: 2025-11-09

---

## Problem

When building BepInEx preloader patchers that need to intercept assembly loading, installing hooks too early can interfere with BepInEx's own initialization process, causing crashes or failures.

---

## Context

BepInEx preloader patchers have two lifecycle methods:
- `Initialize()` - Called early during BepInEx startup
- `Finalizer()` - Called after BepInEx completes initialization

Between these two phases, BepInEx performs critical operations like:
- Preloading interop assemblies (e.g., 104 assemblies in IL2CPP games)
- Setting up the chainloader
- Initializing core BepInEx systems

Installing runtime hooks (like `AssemblyLoadContext.Resolving`) during `Initialize()` can intercept and interfere with these BepInEx operations.

---

## Solution

**Separate initialization concerns by lifecycle phase:**

### Use Initialize() for Static Modifications
- Harmony patches to existing code
- Cecil IL patching
- Static configuration
- Logger setup
- Validation checks

**Why safe**: These don't intercept runtime operations, just modify code before execution.

### Use Finalizer() for Runtime Hooks
- AssemblyLoadContext.Resolving handlers
- AppDomain.AssemblyResolve handlers
- Event subscriptions that intercept framework operations
- File system watchers
- Network listeners

**Why safe**: BepInEx has completed initialization, hook won't interfere.

---

## Example

### ❌ Incorrect (Causes BepInEx Interference)

```csharp
[PatcherPluginInfo("com.example.mypatcher", "MyPatcher", "1.0.0")]
public class MyPatcher : BasePatcher
{
    private static ManualLogSource Logger;

    public override void Initialize()
    {
        Logger = BepInEx.Logging.Logger.CreateLogSource("MyPatcher");

        // Harmony patches - OK here
        var harmony = new Harmony("com.example.mypatcher");
        harmony.Patch(...);

        // ❌ BAD: Installing runtime hook too early!
        // This will intercept BepInEx's own assembly loading
        AssemblyLoadContext.Default.Resolving += OnAssemblyResolving;

        Logger.LogInfo("Patcher initialized");
    }

    public override void Finalizer()
    {
        Logger.LogInfo("Patcher finalized");
    }

    private static Assembly OnAssemblyResolving(AssemblyLoadContext context, AssemblyName name)
    {
        // This runs during BepInEx initialization - can cause crashes!
        return null;
    }
}
```

**Problem**: The hook intercepts BepInEx's assembly preloading, causing crashes.

---

### ✅ Correct (Proper Lifecycle Separation)

```csharp
[PatcherPluginInfo("com.example.mypatcher", "MyPatcher", "1.0.0")]
public class MyPatcher : BasePatcher
{
    private static ManualLogSource Logger;
    private static Harmony harmony;

    public override void Initialize()
    {
        Logger = BepInEx.Logging.Logger.CreateLogSource("MyPatcher");

        // ✅ GOOD: Harmony patches in Initialize()
        // These are static modifications, no runtime side effects
        harmony = new Harmony("com.example.mypatcher");
        harmony.Patch(...);

        Logger.LogInfo("Static patches installed");
        Logger.LogInfo("Runtime hooks will be activated in Finalizer phase");
    }

    public override void Finalizer()
    {
        // ✅ GOOD: Runtime hook in Finalizer()
        // BepInEx has completed initialization, safe to install now
        AssemblyLoadContext.Default.Resolving += OnAssemblyResolving;

        Logger.LogInfo("Runtime hooks installed successfully");
        Logger.LogInfo("Patcher initialization complete");
    }

    private static Assembly OnAssemblyResolving(AssemblyLoadContext context, AssemblyName name)
    {
        // Now only intercepts game/mod assembly loading, not BepInEx
        return null;
    }
}
```

**Benefits**:
- BepInEx can complete initialization without interference
- Clear separation of concerns
- Predictable hook activation timing
- Easier to debug and maintain

---

## When to Apply This Pattern

### Apply When:
- ✅ Hooking AssemblyLoadContext.Resolving
- ✅ Hooking AppDomain.AssemblyResolve (legacy)
- ✅ Intercepting file system operations that BepInEx might use
- ✅ Installing event handlers on framework objects
- ✅ Setting up background threads or timers
- ✅ Opening network connections

### Not Needed When:
- ❌ Only using Harmony patches (can stay in Initialize())
- ❌ Only using Cecil IL patching (can stay in Initialize())
- ❌ Only reading configuration files (can stay in Initialize())
- ❌ Only setting up logging (can stay in Initialize())
- ❌ Only validating environment (can stay in Initialize())

---

## Decision Tree

```
Does your patcher need to:
  ├─ Modify existing code (Harmony/Cecil)?
  │  └─ YES → Use Initialize()
  │
  └─ Intercept runtime operations?
     ├─ YES → Use Finalizer()
     └─ NO → Use Initialize()

Need both?
  └─ YES → Initialize() for patches, Finalizer() for hooks
```

---

## Benefits

1. **Prevents BepInEx Interference**
   - BepInEx can complete initialization unimpeded
   - No chainloader crashes
   - No assembly preload failures

2. **Clear Separation of Concerns**
   - Static modifications: Initialize()
   - Runtime interception: Finalizer()
   - Easy to understand code organization

3. **Predictable Behavior**
   - Hook activation timing is deterministic
   - No race conditions with BepInEx initialization
   - Easier to debug issues

4. **Better Compatibility**
   - Works with all BepInEx plugins
   - No conflicts with other patchers
   - Future-proof against BepInEx changes

---

## Trade-offs

### Advantages
- ✅ No interference with BepInEx initialization
- ✅ Cleaner code separation
- ✅ More predictable behavior
- ✅ Better debugging experience

### Disadvantages
- ⚠️ Hook not active during early preloader phase
  - **Mitigation**: Most assemblies load during game initialization, not preload
  - **Impact**: Usually negligible - test to confirm

- ⚠️ Slightly more complex initialization logic
  - **Mitigation**: Follow pattern consistently
  - **Impact**: Minimal - clearer code overall

---

## Consequences

### If Hook Installed Too Early (Initialize)
- BepInEx may fail to preload assemblies
- Chainloader may crash with MissingMethodException
- Assembly resolution errors during startup
- "Preloaded X assemblies" message missing from logs
- Game fails to launch on first run

### If Hook Installed Correctly (Finalizer)
- BepInEx completes initialization successfully
- "Preloaded X assemblies" appears in logs
- Chainloader starts correctly
- Game launches on first run
- Hook only intercepts game/mod assembly loading

---

## Diagnostic Checklist

### If experiencing BepInEx initialization issues:

1. **Check hook installation timing**
   - [ ] Are AssemblyLoadContext hooks in Initialize()? → Move to Finalizer()
   - [ ] Are event handlers installed early? → Move to Finalizer()

2. **Check BepInEx logs for missing messages**
   - [ ] Look for "Preloaded X interop assemblies"
   - [ ] If missing, a hook is interfering

3. **Compare with clean install**
   - [ ] Run game with only BepInEx (no plugins)
   - [ ] Compare log output
   - [ ] Identify what your plugin prevents

4. **Test lifecycle phases**
   - [ ] Move all hooks to Finalizer()
   - [ ] Test if issue resolves
   - [ ] Move back only if necessary

---

## Real-World Example

### InteropRedirector Case Study

**Problem**: First-run crash when MelonLoader + BepInEx both present

**Root Cause**: AssemblyLoadContext.Resolving hook installed in Initialize() prevented BepInEx from preloading 104 interop assemblies.

**Solution**: Moved hook to Finalizer()

**Code**:
```csharp
public override void Initialize()
{
    // Setup logger
    Logger = BepInEx.Logging.Logger.CreateLogSource("InteropRedirector");

    // Install Harmony patches (safe in Initialize)
    harmony = new Harmony("com.bepinex.melonloader.interop_redirector");
    harmony.Patch(typeof(AssemblyLoadContext).GetMethod("ValidateAssemblyNameWithSimpleName", ...),
        prefix: new HarmonyMethod(typeof(InteropRedirectorPatcher), nameof(BypassIl2CppNameValidation)));

    Logger.LogInfo("Harmony patches installed. Resolution hook will be activated in Finalizer phase.");
}

public override void Finalizer()
{
    // Install runtime hook (safe in Finalizer)
    AssemblyLoadContext.Default.Resolving += OnAssemblyResolving;
    _handlerInstalled = true;
    Logger.LogInfo("Assembly resolution hook installed successfully");
}
```

**Result**:
- ✅ BepInEx preloaded 104 assemblies successfully
- ✅ No chainloader crash
- ✅ First-run works
- ✅ Second-run works without regression

**Reference**: See `.claude/knowledge/investigations/first_run_elimination.md`

---

## Related Patterns

### Lazy Initialization Pattern
When combined with lifecycle timing:
```csharp
private static Lazy<MyState> lazyState = new Lazy<MyState>(
    () => InitializeState(),
    LazyThreadSafetyMode.ExecutionAndPublication
);

public override void Finalizer()
{
    // Hook installed, trigger lazy initialization on first use
    AssemblyLoadContext.Default.Resolving += OnAssemblyResolving;
}

private static Assembly OnAssemblyResolving(...)
{
    // Access lazy state only when needed
    var state = lazyState.Value;
    // ...
}
```

### Event Handler Cleanup Pattern
```csharp
private volatile bool _handlerInstalled;

public override void Finalizer()
{
    AssemblyLoadContext.Default.Resolving += OnAssemblyResolving;
    _handlerInstalled = true;
}

public void Dispose()
{
    if (_handlerInstalled)
    {
        AssemblyLoadContext.Default.Resolving -= OnAssemblyResolving;
        _handlerInstalled = false;
    }
}
```

---

## Anti-Patterns to Avoid

### ❌ Static Constructor Hook Installation
```csharp
static MyPatcher()
{
    // DON'T: Unpredictable timing, runs before Logger available
    AssemblyLoadContext.Default.Resolving += ...;
}
```

**Why bad**: Timing unpredictable, no Logger, hard to debug

### ❌ Initialize() with "Detect and Skip" Logic
```csharp
public override void Initialize()
{
    AssemblyLoadContext.Default.Resolving += (ctx, name) =>
    {
        // DON'T: Complex logic to detect and skip BepInEx operations
        if (IsBepInExAssembly(name))
            return null;
        return ResolveCustom(name);
    };
}
```

**Why bad**: Complex, error-prone, still risks interference, Finalizer() is simpler

### ❌ Delayed Hook via Task/Timer
```csharp
public override void Initialize()
{
    // DON'T: Race conditions, unpredictable timing
    Task.Delay(1000).ContinueWith(_ =>
    {
        AssemblyLoadContext.Default.Resolving += ...;
    });
}
```

**Why bad**: Race conditions, arbitrary delay, Finalizer() is deterministic

---

## Testing Strategy

### Unit Testing
```csharp
[Test]
public void TestPatcherLifecycle()
{
    var patcher = new MyPatcher();

    // After Initialize, Harmony patches should be installed
    patcher.Initialize();
    Assert.IsTrue(HarmonyPatchExists("MyPatch"));

    // After Finalizer, runtime hooks should be installed
    patcher.Finalizer();
    Assert.IsTrue(patcher.IsHandlerInstalled());
}
```

### Integration Testing
1. Test with clean BepInEx install (no other plugins)
2. Check for "Preloaded X assemblies" in logs
3. Test first-run scenario (delete generated assemblies)
4. Test second-run scenario (assemblies already exist)
5. Compare logs: ensure no missing messages

### Regression Testing
- Test with multiple BepInEx plugins
- Test with multiple preloader patchers
- Monitor for chainloader crashes
- Verify assembly load order

---

## References

### Documentation
- [BepInEx Preloader Patchers](https://docs.bepinex.dev/articles/dev_guide/preloader_patchers.html)
- [AssemblyLoadContext.Resolving Event](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblyloadcontext.resolving)

### Related Files
- Investigation: `.claude/knowledge/investigations/first_run_elimination.md`
- Implementation: `/home/smethan/MelonLoaderLoader/BepInEx.MelonLoader.InteropRedirector/InteropRedirectorPatcher.cs`

### Community Resources
- BepInEx Discord: https://discord.gg/MpFEDAg
- GitHub Discussions: https://github.com/BepInEx/BepInEx/discussions

---

## Version History

- **v1.0** (2025-11-09): Initial pattern documentation based on InteropRedirector fix
- Pattern extracted from first-run elimination investigation
- Validated through production testing

---

**Pattern Status**: ✅ VALIDATED IN PRODUCTION
**Applicability**: BepInEx 6.x preloader patchers
**Risk Level**: Low (when followed correctly)
**Recommended**: Yes (for all assembly hook use cases)
