# Context Preservation Instructions for context-manager Agent

## Mission
Preserve the current session state in a structured, restorable format that enables seamless continuation across sessions and machines.

## Target Directory Structure
Save files to the following locations based on content type:

### Session State (EPHEMERAL - Not tracked in git)
**Location**: `.claude/context/active/`
- `session_{SESSION_NUMBER}_{TIMESTAMP}.md` - Complete session snapshot
- Include EVERYTHING needed to restore context

### Core Knowledge (TRACKED - Committed to git)
**Location**: `.claude/project/`
- `ARCHITECTURE.md` - System design and component overview
- `TECHNICAL_DECISIONS.md` - Architecture Decision Records (ADRs)
- `CONSTRAINTS.md` - Technical constraints and requirements
- Update these ONLY if new architectural decisions were made

### Patterns (TRACKED - Committed to git)
**Location**: `.claude/knowledge/patterns/`
- `{pattern_name}.md` - Reusable patterns discovered during work
- Create new files ONLY if a new reusable pattern emerged
- Examples: `harmony_patching.md`, `assembly_resolution.md`

### Investigations (TRACKED when complete)
**Location**: `.claude/knowledge/investigations/`
- `{topic}.md` - Completed root cause analyses and investigations
- Move here ONLY when investigation is resolved
- Examples: `net6_validation_bypass.md`, `two_run_elimination.md`

### Version Documentation (TRACKED - Committed to git)
**Location**: `.claude/versions/v{VERSION}/`
- `RELEASE_NOTES.md` - User-facing release notes
- `IMPLEMENTATION.md` - Technical implementation details
- Update ONLY for version milestones

## Current Session Context
**Session Number**: {SESSION_NUMBER}
**Timestamp**: {TIMESTAMP}
**Current Task**: {CURRENT_TASK}
**Git Branch**: {GIT_BRANCH}
**Git Commit**: {GIT_COMMIT}
**Milestone Save**: {IS_MILESTONE}

## What to Capture

### 1. Session Snapshot (REQUIRED - Always create)
**File**: `.claude/context/active/session_{SESSION_NUMBER}_{TIMESTAMP}.md`

**Structure**:
```markdown
# Session {SESSION_NUMBER} - {TIMESTAMP}

## Current Task
[What user is working on right now]

## Recent Changes
[Files modified, commits made]

## Work Completed
[What was accomplished this session]

## Pending Work
[What remains to be done - be specific]

## Technical Context
### Architectural Decisions Made
[Any decisions about system design]

### Code Patterns Used
[Patterns or conventions established]

### Rejected Alternatives
[What was tried but didn't work, and why]

### Key Insights
[Important realizations or discoveries]

## RESTORATION_INSTRUCTIONS
### Quick Start (5 minutes)
1. [First thing to do when resuming]
2. [Second thing to check]
3. [Where to continue from]

### Detailed Context (15 minutes)
1. [More thorough restoration steps]
2. [Files to review]
3. [Context to rebuild]

### Success Criteria
- [ ] [What needs to be true to consider task complete]
- [ ] [Another success criterion]

## Code References
[File paths and line numbers for key changes]
- `file.cs:123-145` - Description of what's there
- `another.cs:67` - Important code location

## TodoWrite State
[Current todo list if applicable]
\`\`\`json
{TODOS_JSON}
\`\`\`

## Metadata
- Session number: {SESSION_NUMBER}
- Duration: [Estimate based on conversation]
- Git status: [Current branch and commit]
- Files modified: [Count and list]
```

### 2. Knowledge Promotion (CONDITIONAL - Only if new knowledge emerged)

**When to create**: Only if this session produced reusable knowledge

#### Pattern Discovery
**File**: `.claude/knowledge/patterns/{pattern_name}.md`

**Create if**: A reusable pattern or approach was established

**Structure**:
```markdown
# Pattern: {Pattern Name}

## Context
[When/why this pattern is used]

## Problem
[What problem this solves]

## Solution
[The pattern/approach]

## Example
\`\`\`csharp
// Code example
\`\`\`

## Benefits
- Benefit 1
- Benefit 2

## Risks
- Risk 1
- Risk 2

## References
- Related file: `path/to/file.cs:lines`
- Related investigation: `investigations/topic.md`
```

#### Investigation Completion
**File**: `.claude/knowledge/investigations/{topic}.md`

**Create if**: A bug investigation or root cause analysis was COMPLETED

**Structure**:
```markdown
# Investigation: {Topic}

## Problem Statement
[Clear description of the issue]

## Root Cause
[What was actually wrong]

## Solution
[How it was fixed]

## Implementation
- File: `path/to/file.cs:lines`
- Changes: [Description]

## Validation
[How the fix was verified]

## Lessons Learned
[What to remember for future]
```

#### Architectural Decision Record (ADR)
**File**: `.claude/project/TECHNICAL_DECISIONS.md` (APPEND)

**Create if**: A significant architectural decision was made

**Append**:
```markdown
---

## ADR-{NUMBER}: {Decision Title}
**Date**: {DATE}
**Status**: Accepted | Rejected | Superseded

### Context
[Situation and requirements]

### Decision
[What was decided]

### Consequences
**Positive**:
- Pro 1
- Pro 2

**Negative**:
- Con 1
- Con 2

### Alternatives Considered
1. **Alternative 1**: Rejected because...
2. **Alternative 2**: Rejected because...
```

## Critical Rules

### File Naming
- Session files: `session_{NUMBER}_{TIMESTAMP}.md`
- Pattern files: `{lowercase_with_underscores}.md`
- Investigation files: `{topic_name}.md`
- Timestamps: `YYYY-MM-DD_HHmmss` format

### When to Track in Git
- ✅ ALWAYS track: `project/`, `knowledge/`, `versions/`, `templates/`
- ❌ NEVER track: `context/active/`, `context/archive/`
- 📋 Session files stay local (not committed to git)
- 📋 Only promote to tracked locations when knowledge is complete/reusable

### Content Quality
- Be specific: Include file paths, line numbers, exact function names
- Be actionable: Restoration instructions should be step-by-step
- Be honest: Document what didn't work, not just successes
- Be complete: Capture enough context to restore WITHOUT reading entire conversation

### Restoration Focus
The PRIMARY goal is that someone reading ONLY the session file can:
1. Understand what was being worked on
2. Know where the code is (files and line numbers)
3. Continue the work without re-reading conversation history
4. Understand why decisions were made

## Output Format

After saving files, provide a summary:

```
✅ Context Preserved

Session State (Local):
- `.claude/context/active/session_{N}_{TIMESTAMP}.md`

Knowledge Captured (Tracked):
- [List any files created/updated in project/, knowledge/, versions/]
- [Or "None - session state only" if nothing to promote]

Restoration:
To restore this session:
1. Read: `.claude/context/active/session_{N}_{TIMESTAMP}.md`
2. [Any additional restoration steps]

Next Session Number: {N+1}
```

## Example Invocation

**User says**: "Save context - I'm at 60% usage"

**You analyze**:
- Current task: "Implementing Harmony patch for assembly validation"
- Files modified: `InteropRedirectorPatcher.cs` lines 96-113, 346-404
- New pattern discovered: "Harmony prefix patching for .NET internals"
- Investigation completed: ".NET 6 validation bypass"

**You create**:
1. `.claude/context/active/session_7_2025-11-09_163042.md` (full session state)
2. `.claude/knowledge/patterns/harmony_patching.md` (new pattern)
3. `.claude/knowledge/investigations/net6_validation_bypass.md` (completed investigation)
4. Append to `.claude/project/TECHNICAL_DECISIONS.md` (ADR for Harmony approach)

## Begin Preservation

Current session context:
- Session: {SESSION_NUMBER}
- Task: {CURRENT_TASK}
- Timestamp: {TIMESTAMP}
- Milestone: {IS_MILESTONE}

Analyze the conversation and create the appropriate files following the structure above.

---

## ADDITIONAL INSTRUCTIONS: Milestone Save

**ONLY execute this section if `{IS_MILESTONE} = true`**

If this is a milestone save, ALSO update `.claude/PROJECT_STATUS.md`:

### Update PROJECT_STATUS.md

1. **Read** `.claude/PROJECT_STATUS.md`

2. **Update these fields**:
   - `**Last Updated**:` → {TIMESTAMP} (date only: YYYY-MM-DD)
   - `**Current Version**:` → Parse from recent ADRs or code
   - `**Status**:` → Based on work state (Stable/In Development/Released)

3. **Update "Recent Architectural Decisions" section**:
   - Read `.claude/project/TECHNICAL_DECISIONS.md`
   - List last 3-4 most recent ADRs with their status
   - Format: `- ADR-NNN: Title (STATUS)`

4. **Update "Pending Work" section**:
   - Based on PROPOSED ADRs from TECHNICAL_DECISIONS.md
   - Based on current conversation context
   - Keep Priority 1-3 structure
   - Update estimated times if known

5. **Preserve unchanged**:
   - Project overview
   - Technical context
   - Key Files section (unless paths changed)
   - Development Commands
   - All formatting and structure

6. **Write** the updated PROJECT_STATUS.md

### Example Update (Milestone Save)

```diff
- **Last Updated**: 2025-11-10
+ **Last Updated**: 2025-11-12
- **Current Version**: v2.3.0
+ **Current Version**: v2.3.1
- **Status**: Stable - Ready for v2.3.1 bug fix release
+ **Status**: Released - v2.3.1 deployed

## Recent Architectural Decisions

- ADR-003: Bypass ValidateAssemblyNameWithSimpleName (ACCEPTED)
- ADR-004: Accept two-run requirement (ACCEPTED)
- ADR-005: Fix assembly validation logic (PROPOSED)
+ ADR-005: Fix assembly validation logic (ACCEPTED - v2.3.1)

## Pending Work (Priority Order)

### Priority 1: Fix Validation Bug (15-30 mins)
- **Status**: PROPOSED in ADR-005
+ **Status**: COMPLETED in ADR-005 (v2.3.1)
```

**Critical**: Only update PROJECT_STATUS.md if `{IS_MILESTONE} = true`. For standard saves, skip this section entirely.
