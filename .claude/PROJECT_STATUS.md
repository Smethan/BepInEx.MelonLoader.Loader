# BepInEx.MelonLoader.Loader - Current Project Status

**Last Updated**: 2025-11-10
**Current Version**: v2.3.0
**Status**: Stable - Ready for v2.3.1 bug fix release

---

## Quick Start (First Time Using Claude Code?)

1. **Read this file** for project overview
2. **Run `/load-context`** to get detailed state from ADRs
3. **Check TECHNICAL_DECISIONS.md** for recent architectural decisions
4. **Review pending work** below

---

## What This Project Does

BepInEx 6 plugin that loads MelonLoader for IL2CPP Unity games, enabling:
- BepInEx and MelonLoader mods to coexist
- Assembly redirection from BepInEx → MelonLoader's enhanced versions
- Harmony patching to bypass .NET assembly name validation

---

## Current State Summary

### ✅ Working (v2.3.0)
- Harmony patch bypasses assembly name validation (ADR-003)
- Hook installation in Finalizer() allows BepInEx preloading
- NotifyMelonLoaderReady() re-initialization pattern
- Supports r2modman and manual installations

### ⚠️ Known Issues
1. **False validation warnings** - Fixed in ADR-005 (ready to implement)
2. **Two-run requirement on first install** - Architectural, cannot be eliminated (ADR-004)

---

## Recent Architectural Decisions

See `.claude/project/TECHNICAL_DECISIONS.md` for full details:

- **ADR-001**: Use Harmony for .NET internal patching (ACCEPTED)
- **ADR-002**: Defer hook to Finalizer phase (ACCEPTED)
- **ADR-003**: Bypass ValidateAssemblyNameWithSimpleName (ACCEPTED)
- **ADR-004**: Accept two-run requirement as architectural reality (ACCEPTED)
- **ADR-005**: Fix assembly validation logic (PROPOSED - ready to implement)

---

## Pending Work (Priority Order)

### Priority 1: Fix Validation Bug (15-30 mins)
**Status**: PROPOSED in ADR-005
**File**: `BepInEx.MelonLoader.InteropRedirector/InteropRedirectorPatcher.cs:756-768`
**Change**: Directory-based validation instead of exact filename match
**Impact**: Eliminates false warnings about assembly location mismatches

### Priority 2: Improve User Messaging (5-10 mins)
**Status**: PROPOSED in ADR-005
**Change**: Add first-run detection message clarifying restart requirement
**Impact**: Better UX for first-time setup

### Priority 3: Release v2.3.1 (10 mins)
**Status**: Ready after Priorities 1-2
**Command**: `./build.sh DevDeploy` (local) or `./build.sh` (CI)
**Impact**: Bug fix release with cleaner logs

---

## Key Files and Line Numbers

### Main Implementation
- `BepInEx.MelonLoader.InteropRedirector/InteropRedirectorPatcher.cs`
  - Lines 89-125: Initialize() - Harmony patch installation
  - Lines 127-145: Finalizer() - Hook installation (previous fix)
  - Lines 756-768: ValidateLoadedAssembly() - **HAS BUG, needs fixing**
  - Lines 186-231: CreateAssemblyAliases() - Assembly name aliasing

- `BepInEx.MelonLoader.Loader.IL2CPP/Plugin.cs`
  - Load() method: MelonLoader bootstrap in Plugin phase
  - NotifyMelonLoaderReady() call triggers re-initialization

### Documentation
- `.claude/project/TECHNICAL_DECISIONS.md` - All ADRs
- `.claude/project/ARCHITECTURE.md` - System design (if exists)
- `.claude/CLAUDE.md` - Main project instructions and workflow

---

## Technical Context

### Assembly Loading Flow
```
[T+100ms] PATCHER PHASE
└─ InteropRedirectorPatcher.Finalizer()
   ├─ Install Harmony patch for validation bypass ✓
   └─ Install AssemblyLoadContext.Resolving hook ✓

[T+500ms] PLUGIN PHASE
└─ MelonLoader.Loader.Plugin.Load()
   ├─ MelonLoader initializes ✓
   ├─ Il2CppAssemblyGenerator runs (first run only, 5-30s)
   └─ NotifyMelonLoaderReady() → InteropRedirector re-initializes ✓
```

### Key Constraints
- Must maintain BepInEx + MelonLoader compatibility
- Support r2modman and manual installations
- Two-run requirement on first install is unavoidable (architectural)
- Prefer .NET 6+ APIs (AssemblyLoadContext over AppDomain)

---

## Development Commands

### Build
```bash
./build.sh              # Standard build
./build.sh DevDeploy    # Build + deploy to test environment
```

### Logs
- **Local**: `/mnt/c/Users/Ethan/AppData/Roaming/r2modmanPlus-local/Megabonk/profiles/Default/BepInEx/LogOutput.log`
- **Remote**: Ask user for log file

### Context Management
```bash
/save-context           # Preserve session at 60% context usage
/load-context           # Restore from session or tracked files
```

---

## Next Steps for You

1. **Immediate**: Implement ADR-005 validation fix (15-30 mins)
2. **Then**: Add user messaging improvements (5-10 mins)
3. **Finally**: Build and release v2.3.1 (10 mins)

**Total estimated time**: 30-50 minutes for complete v2.3.1 release

---

## Questions?

- **Full project instructions**: Read `.claude/CLAUDE.md`
- **Architectural decisions**: Read `.claude/project/TECHNICAL_DECISIONS.md`
- **Detailed investigations**: Check `.claude/knowledge/investigations/`
- **Session history**: Run `/load-context` (will use fallback if no session files)

**Ready to start?** Say: "Ready to implement ADR-005" or run `/load-context` for full details.
