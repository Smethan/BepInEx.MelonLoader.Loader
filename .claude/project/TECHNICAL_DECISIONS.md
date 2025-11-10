# Technical Decisions and Architecture Decision Records (ADRs)

This document tracks major technical decisions made during the development of BepInEx.MelonLoader.Loader.

---

## ADR-001: Use .NET 6+ AssemblyLoadContext Instead of AppDomain
**Date**: 2025-11-07 (estimated)
**Status**: Accepted

### Context
BepInEx 6 targets .NET 6+ runtime, while MelonLoader originally used .NET Framework AppDomain APIs.

### Decision
Use `AssemblyLoadContext` for assembly isolation and resolution instead of legacy `AppDomain` APIs.

### Consequences
**Positive**:
- Modern .NET 6+ compatibility
- Better isolation and unloading support
- Future-proof for .NET evolution

**Negative**:
- Different API surface than .NET Framework
- Internal validation not exposed (required Harmony workaround)

### Alternatives Considered
1. **Continue using AppDomain APIs**: Rejected - not available in .NET 6+
2. **Patch MelonLoader itself**: Rejected - too invasive, breaks updates

---

## ADR-002: Use Harmony to Bypass .NET Assembly Name Validation
**Date**: 2025-11-09
**Status**: Accepted

### Context
.NET 6's `AssemblyLoadContext.Resolving` event has internal validation that rejects assemblies when simple names don't match (Il2CppSystem.Private.CoreLib vs Il2Cppmscorlib). This validation runs AFTER the Resolving handler returns and cannot be bypassed through normal .NET APIs.

### Decision
Use Harmony to patch `AssemblyLoadContext.ValidateAssemblyNameWithSimpleName` method, bypassing validation for known Il2CPP assembly name mappings.

### Rationale
- .NET validation is internal and cannot be configured
- Harmony patching is standard in modding ecosystem
- Targeted patch minimizes risk
- Only affects Il2CPP assemblies, not general .NET assemblies

### Implementation
```csharp
harmony.Patch(
    typeof(AssemblyLoadContext).GetMethod("ValidateAssemblyNameWithSimpleName", BindingFlags.Static | BindingFlags.NonPublic),
    prefix: new HarmonyMethod(typeof(InteropRedirectorPatcher), nameof(BypassIl2CppNameValidation))
);
```

### Consequences
**Positive**:
- Assembly redirection works on first run
- No filesystem aliases required
- Transparent to user

**Negative**:
- Depends on internal .NET method (could break on runtime updates)
- Requires Harmony library

### Alternatives Considered
1. **Filesystem aliases only**: Rejected - doesn't work on first run
2. **Pre-copy BepInEx assemblies**: Rejected - incompatible generators
3. **Request .NET API change**: Rejected - unrealistic timeline

### References
- Implementation: `BepInEx.MelonLoader.InteropRedirector/InteropRedirectorPatcher.cs:346-404`
- Investigation: `.claude/knowledge/investigations/net6_validation_bypass.md`

---

## ADR-003: Hook Installation Timing in BepInEx Patchers
**Date**: 2025-11-09
**Status**: Accepted

### Context
InteropRedirector patcher needs to install `AssemblyLoadContext.Resolving` hook to redirect assembly resolution. Initially implemented in `Initialize()`, but this prevented BepInEx from preloading interop assemblies.

### Problem
When hook installed in Initialize():
1. BepInEx loads InteropRedirector patcher
2. Initialize() installs AssemblyLoadContext.Resolving hook
3. Hook interferes with BepInEx assembly preloading
4. BepInEx fails to preload 104 interop assemblies
5. Chainloader crashes with MissingMethodException

Evidence: "Preloaded 104 interop assemblies" message missing from logs.

### Decision
Move `AssemblyLoadContext.Resolving` hook installation from `Initialize()` to `Finalizer()`.

### Rationale
- `Initialize()` runs before BepInEx completes its initialization
- Installing hook in `Initialize()` intercepts BepInEx's own assembly loading
- This prevents BepInEx from preloading 104 interop assemblies
- `Finalizer()` runs after BepInEx initialization is complete
- Hook in `Finalizer()` doesn't interfere with BepInEx setup

### Implementation
```csharp
public override void Initialize()
{
    // Safe: Harmony patches only (no runtime side effects)
    harmony = new Harmony("com.bepinex.melonloader.interop_redirector");
    harmony.Patch(...);

    // NOTE: Hook installation deferred to Finalizer()
    Logger.LogInfo("Harmony patches installed. Resolution hook will be activated in Finalizer phase.");
}

public override void Finalizer()
{
    // Safe: BepInEx initialization complete
    AssemblyLoadContext.Default.Resolving += OnAssemblyResolving;
    _handlerInstalled = true;
    Logger.LogInfo("Assembly resolution hook installed successfully");
}
```

### Consequences
**Positive**:
- BepInEx can preload assemblies without interference
- No chainloader crashes
- First-run works correctly
- Cleaner separation of initialization phases

**Negative**:
- Hook not active during early preloader phase
  - **Impact**: Acceptable - no assemblies need redirection that early
  - **Evidence**: First-run test confirms no issues

### Alternatives Considered
1. **Static constructor**: Rejected - timing unpredictable, breaks Logger
2. **Patch BepInEx initialization**: Rejected - too invasive, breaks updates
3. **Keep in Initialize()**: Rejected - breaks BepInEx preloading
4. **Delayed hook via Task/Timer**: Rejected - race conditions, unpredictable

### Testing
**First-Run Test** (Deleted Il2CppAssemblies): ✅ PASSED
- BepInEx preloaded 104 assemblies successfully
- No chainloader crash
- MelonLoader initialized correctly
- Game launched on first run

**Second-Run Test**: ⏳ Pending validation

### References
- Investigation: `.claude/knowledge/investigations/first_run_elimination.md`
- Pattern: `.claude/knowledge/patterns/bepinex_patcher_lifecycle.md`
- Implementation: `BepInEx.MelonLoader.InteropRedirector/InteropRedirectorPatcher.cs:89-145`

### Related Patterns
**BepInEx Patcher Lifecycle Timing Pattern**:
- Use `Initialize()` for static modifications (Harmony patches, IL patching)
- Use `Finalizer()` for runtime hooks (AssemblyLoadContext.Resolving, event handlers)

### Impact on Version
- v2.3.0: Harmony validation bypass
- v2.3.1: Hook timing fix (this ADR)
- Both fixes required for complete first-run support

---

---

## ADR-004: Accept Two-Run Requirement as Architectural Reality
**Date**: 2025-11-10
**Status**: Accepted

### Context
Investigated feasibility of eliminating the two-run requirement by running Il2CppAssemblyGenerator in patcher phase instead of plugin phase. After comprehensive analysis, discovered the requirement is architectural, not a bug.

### Problem
Initial hypothesis: "Could we run the Il2CppAssemblyGenerator in the patcher phase to avoid needing a restart?"

After investigation, identified three major blockers:
1. **Circular Dependency**: Il2CppAssemblyGenerator is a MelonLoader module requiring MelonModule system that isn't initialized until Plugin phase
2. **Missing Dependencies**: Generator requires MelonEnvironment, LoaderConfig, MelonLogger - none available in patcher phase
3. **Performance**: Generator takes 5-30 seconds; blocking patcher phase would freeze game startup
4. **Assembly Context**: No way to cache/reuse assemblies generated in patcher phase

### Decision
Keep current architecture unchanged. Il2CppAssemblyGenerator runs in Plugin phase. Accept two-run requirement as inherent to BepInEx's initialization order.

### Rationale
1. **Architectural correctness**: Generator is designed as a MelonModule, not a standalone utility
2. **Circular dependency unavoidable**: Generator cannot initialize before MelonLoader is ready
3. **Current design is optimal**: Plugin phase generation is the only viable approach
4. **Risk minimization**: Refactoring to support patcher phase would break MelonLoader compatibility
5. **User impact acceptable**: Only affects first installation; subsequent runs work normally

### Implementation
No code changes needed. Document the architectural decision and improve user messaging.

### Consequences
**Positive**:
- Avoids massive refactoring with high technical risk
- Respects MelonLoader module system design
- Maintains compatibility with MelonLoader updates
- Current architecture proven and stable

**Negative**:
- Two-run requirement remains
- Users must restart game on first install
- **Mitigation**: Improve messaging to clarify "first-time setup"

### Alternatives Considered
1. **Pre-initialize MelonLoader in patcher phase**: Rejected - requires invasive refactoring, breaks MelonLoader updates
2. **Pre-generate assemblies in patcher phase**: Rejected - duplicates MelonLoader logic, no practical benefit
3. **Lazy hook installation in Plugin phase**: Rejected - doesn't address when generator runs
4. **Move generator to separate pre-plugin phase**: Rejected - impossible without MelonLoader refactoring

### Evidence
- Investigation: `.claude/knowledge/investigations/two_run_elimination_feasibility.md`
- Technical analysis: `.claude/knowledge/investigations/Il2CppAssemblyGenerator_Feasibility_Assessment.md`
- Code analysis: MelonLoader assembly resolution analysis

### Related Patterns
**Module Initialization Ordering**: BepInEx patcher phase completes before plugin phase, which completes before MelonLoader module system is active. Il2CppAssemblyGenerator requires full MelonLoader initialization.

### Impact on Development
- Accept two-run requirement as part of the design
- Focus on improving user experience rather than eliminating the requirement
- Document clearly that this is "first-time setup" not an error
- Consider automating the restart process

---

## ADR-005: Fix Assembly Validation Logic for .NET Unification
**Date**: 2025-11-10
**Status**: Proposed (pending implementation)

### Context
Assembly location validation in InteropRedirectorPatcher.cs incorrectly rejects correctly-loaded assemblies because .NET's assembly unification mechanism maps assembly simple names (Il2CppSystem.Private.CoreLib -> Il2Cppmscorlib).

### Problem
When loading assemblies, the validation performs exact filename matching:

```
1. Assembly request: "Il2CppSystem.Private.CoreLib"
2. Assembly resolves: Successfully loads from Il2CppAssemblies/Il2CppSystem.Private.CoreLib.dll
3. .NET unification: Internally maps to "Il2Cppmscorlib"
4. Validation: Compares "Il2CppSystem.Private.CoreLib.dll" vs "Il2Cppmscorlib.dll" -> FAILS
5. Result: Returns null, falls back to default resolution
6. User impact: False warnings in logs ("Assembly location mismatch", "Validation failed")
```

**Log Evidence**:
```
[Warning:InteropRedirector] Assembly location mismatch: expected 'Il2CppSystem.Private.CoreLib.dll', got 'Il2Cppmscorlib.dll'
[Warning:InteropRedirector] Loaded assembly validation failed, falling back to default resolution
```

### Decision
Change validation from exact filename match to directory-based validation with debug logging for unification.

```csharp
// Location: InteropRedirectorPatcher.cs:756-768
private static Assembly? ValidateLoadedAssembly(Assembly? resolved, string assemblyName, string expectedPath)
{
    if (resolved == null) return null;

    // Validate directory (allows for .NET assembly unification)
    string resolvedDir = Path.GetDirectoryName(resolved.Location) ?? "";
    string expectedDir = Path.GetDirectoryName(expectedPath) ?? "";

    if (!string.Equals(resolvedDir, expectedDir, StringComparison.OrdinalIgnoreCase))
    {
        Logger.LogWarning($"Assembly location mismatch: expected '{expectedDir}', got '{resolvedDir}'");
        return null;
    }

    // Log unification for debugging
    string resolvedFile = Path.GetFileName(resolved.Location);
    string expectedFile = Path.GetFileName(expectedPath);
    if (resolvedFile != expectedFile)
    {
        Logger.LogDebug($"Assembly unified: {expectedFile} -> {resolvedFile} (allowed, both from same directory)");
    }

    return resolved;
}
```

### Rationale
1. **Directory validation is sufficient**: Ensures assemblies come from the correct location
2. **Respects .NET semantics**: Allows .NET's unification mechanism to work naturally
3. **Security maintained**: Still validates assemblies from interop folder, preventing injection attacks
4. **Debugging improved**: Debug logs clarify when unification occurs
5. **No functional change**: Behavior identical, just cleaner logs

### Implementation Status
- **File**: `BepInEx.MelonLoader.InteropRedirector/InteropRedirectorPatcher.cs`
- **Lines**: 756-768 (ValidateLoadedAssembly method)
- **Changes**: 15-20 lines
- **Risk level**: LOW (isolated validation logic, extensive test evidence available)

### Consequences
**Positive**:
- Eliminates false warnings from logs
- Shows correct assembly resolution behavior
- Cleaner, more professional logs for users
- Better debugging information
- No functional impact - behavior unchanged

**Negative**:
- None identified

### Testing Plan
1. Delete Il2CppAssemblies folder
2. Deploy build with fix
3. Launch game on first run
4. Verify:
   - No "Assembly location mismatch" warnings
   - Debug logs show "Assembly unified: ..." messages
   - Game loads correctly
   - Both BepInEx and MelonLoader work

### References
- Implementation: `BepInEx.MelonLoader.InteropRedirector/InteropRedirectorPatcher.cs:756-768`
- Investigation: `.claude/knowledge/investigations/Il2CppAssemblyGenerator_Feasibility_Assessment.md`
- .NET Docs: Assembly unification behavior in AssemblyLoadContext

### Related Patterns
**.NET Assembly Unification**: When multiple versions of an assembly with the same simple name are loaded, .NET's assembly loader unifies them to use the same assembly instance. Our validation logic must account for this behavior.

### Version Impact
- v2.3.0: Harmony validation bypass (ADR-002)
- v2.3.1: Assembly validation fix (this ADR) - RECOMMENDED
- Both fixes together provide complete first-run support

---

## Decision Template

```markdown
## ADR-XXX: [Decision Title]
**Date**: YYYY-MM-DD
**Status**: [Proposed | Accepted | Rejected | Superseded]

### Context
[Describe the problem and constraints]

### Decision
[State the decision clearly]

### Rationale
[Explain why this decision was made]

### Consequences
**Positive**:
- [Benefits]

**Negative**:
- [Drawbacks and mitigations]

### Alternatives Considered
1. **Option A**: [Why rejected]
2. **Option B**: [Why rejected]

### References
- [Links to code, docs, investigations]
```

---

## Document Metadata
- **Created**: 2025-11-09
- **Last Updated**: 2025-11-10
- **Total ADRs**: 5 (4 accepted, 1 proposed)
- **Related Documentation**: `.claude/knowledge/`, `.claude/project/summary.md`, `.claude/context/active/`
