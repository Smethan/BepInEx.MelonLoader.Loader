# Context Restoration Instructions for context-manager Agent

## Mission
Load and summarize context from a previous session to enable seamless continuation of work.

## Source File
**Session File**: {SESSION_FILE}

## Task

1. **Read the session file** at the path specified above
2. **Extract key information**:
   - What task was being worked on
   - What was accomplished
   - What remains to be done
   - Any architectural decisions made
   - Code locations (files and line numbers)
   - RESTORATION_INSTRUCTIONS section

3. **Check for related knowledge**:
   - Look in `.claude/project/` for architectural context
   - Look in `.claude/knowledge/patterns/` for relevant patterns
   - Look in `.claude/knowledge/investigations/` for related investigations
   - Look in `.claude/versions/{current_version}/` for version-specific context

4. **Provide a restoration summary** in this format:

```markdown
# Session Restored: {SESSION_NUMBER}

## Previous Session Summary
**Date**: {DATE}
**Duration**: {DURATION}
**Status**: {STATUS}

## What Was Being Worked On
{TASK_DESCRIPTION}

## Progress Made
{ACCOMPLISHMENTS_LIST}

## Current State
**Files Modified**:
- `path/to/file.cs:123-145` - Description
- `another/file.cs:67` - Description

**Key Code Locations**:
- {IMPORTANT_LOCATIONS}

**Git Status**:
- Branch: {BRANCH}
- Last commit: {COMMIT}

## What's Next
**Immediate Actions**:
1. {NEXT_STEP_1}
2. {NEXT_STEP_2}
3. {NEXT_STEP_3}

**Pending Work**:
- [ ] {PENDING_TASK_1}
- [ ] {PENDING_TASK_2}

## Technical Context
**Architectural Decisions**:
{DECISIONS_SUMMARY}

**Patterns Used**:
{PATTERNS_LIST}

**Constraints**:
{CONSTRAINTS_LIST}

## Related Knowledge
**Relevant Patterns**:
- [Link to pattern files if any]

**Related Investigations**:
- [Link to investigation files if any]

**Version Documentation**:
- Current version: {VERSION}
- Version docs: `.claude/versions/{VERSION}/`

## Success Criteria
- [ ] {CRITERION_1}
- [ ] {CRITERION_2}

## Quick Reference
**Key Files**:
- {FILE_1} - {DESCRIPTION}
- {FILE_2} - {DESCRIPTION}

**Important Insights**:
- {INSIGHT_1}
- {INSIGHT_2}
```

## Output Guidelines

### Brevity
- Summary should be concise but complete
- Focus on actionable information
- Highlight critical decisions and code locations

### Context
- Include enough background to understand WHY decisions were made
- Link to related knowledge files when relevant
- Note any rejected alternatives to avoid repeating mistakes

### Actionability
- Restoration instructions should be step-by-step
- Code references should include file paths and line numbers
- Next actions should be concrete and specific

## Error Handling

If session file doesn't exist:
```
❌ Session file not found: {SESSION_FILE}

Available sessions:
{LIST_OF_SESSION_FILES}

Suggestion: Use the most recent session or specify a different session number.
```

If session file has no RESTORATION_INSTRUCTIONS:
```
⚠️ No restoration instructions found in session file

Falling back to full file analysis...
[Provide best-effort summary from full file content]
```

## Additional Context to Check

After loading the session file, also check:

1. **Git Status**: `git status` to see current uncommitted changes
2. **Recent Commits**: `git log --oneline -5` to see recent work
3. **Branch**: `git branch --show-current` for current branch
4. **Version Docs**: Latest version folder in `.claude/versions/`

Include this in the restoration summary to give complete picture.

## Example Output

```
# Session Restored: 7

## Previous Session Summary
**Date**: 2025-11-09
**Duration**: ~45 minutes
**Status**: In progress - context preserved at 60% usage

## What Was Being Worked On
Implementing Harmony patch to bypass .NET 6's internal assembly name validation
that was rejecting Il2CPP assemblies with mismatched simple names.

## Progress Made
✅ Identified root cause: `AssemblyLoadContext.ValidateAssemblyNameWithSimpleName`
   runs AFTER resolution handler and cannot be bypassed through normal APIs
✅ Implemented Harmony prefix patch at lines 96-113, 346-404
✅ Tested successfully - FileLoadException eliminated
✅ Documented pattern in `.claude/knowledge/patterns/harmony_patching.md`

## Current State
**Files Modified**:
- `BepInEx.MelonLoader.InteropRedirector/InteropRedirectorPatcher.cs:96-113` - Harmony patch installation
- `BepInEx.MelonLoader.InteropRedirector/InteropRedirectorPatcher.cs:346-404` - ValidateBypass prefix method

**Key Code Locations**:
- Line 99: Harmony instance creation
- Line 346: Prefix patch that bypasses validation
- Line 363: Condition check for when to bypass

**Git Status**:
- Branch: master
- Last commit: 09d2d8a "updated interop patch"

## What's Next
**Immediate Actions**:
1. Review git status for uncommitted changes
2. Check logs for "Assembly name validation bypass installed"
3. Continue testing with both BepInEx and MelonLoader mods

**Pending Work**:
- [ ] Test in production game environment
- [ ] Monitor for type conflicts or InvalidCastException
- [ ] Profile performance vs baseline
- [ ] Update v2.3.0 documentation

## Technical Context
**Architectural Decisions**:
- ADR: Use Harmony runtime patching instead of custom ALC implementation
- Rationale: Minimal scope, reversible, works with sealed internals
- Trade-off: Brittle if .NET internals change, but acceptable for v2.3.0

**Patterns Used**:
- Harmony prefix patching (see `.claude/knowledge/patterns/harmony_patching.md`)
- Assembly name mapping for Il2CPP compatibility

**Constraints**:
- Must work with .NET 6+ AssemblyLoadContext
- Cannot modify .NET runtime or configuration
- Must support both BepInEx and MelonLoader simultaneously

## Related Knowledge
**Relevant Patterns**:
- `.claude/knowledge/patterns/harmony_patching.md` - Harmony runtime patching pattern

**Related Investigations**:
- `.claude/knowledge/investigations/net6_validation_bypass.md` - Root cause analysis

**Version Documentation**:
- Current version: v2.3.0
- Version docs: `.claude/versions/v2.3.0/RELEASE_NOTES.md`

## Success Criteria
- [ ] No FileLoadException in logs for Il2CPP assemblies
- [ ] Both BepInEx and MelonLoader mods load successfully
- [ ] Performance within 10% of baseline
- [ ] No type conflicts or InvalidCastException

## Quick Reference
**Key Files**:
- `InteropRedirectorPatcher.cs` - Main implementation with Harmony patch
- `.claude/versions/v2.3.0/RELEASE_NOTES.md` - What's new in v2.3.0
- `.claude/knowledge/patterns/harmony_patching.md` - Pattern reference

**Important Insights**:
- .NET internal validation runs AFTER custom handlers return
- Harmony is necessary to intercept at the right level
- Must set __result and return false to skip original method
```

## Begin Restoration

Read session file: {SESSION_FILE}
Analyze and provide restoration summary following the format above.
