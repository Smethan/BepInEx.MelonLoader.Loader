# Save Context Command

Preserve the current session state using the context-manager agent.

## Usage

```bash
/save-context              # Standard save (session file + ADRs)
/save-context --milestone  # Milestone save (also updates PROJECT_STATUS.md)
```

## Instructions

You MUST use the context-management:context-manager agent to save context. Follow these steps:

1. **Parse Command Arguments**:
   - Check if `--milestone` flag is present
   - If present, set `IS_MILESTONE=true` for later steps

2. **Gather Session Information**:
   - Read `.claude/.session_counter` to get current session number
   - Get timestamp: Current date/time in YYYY-MM-DD_HHmmss format
   - Get git branch: `git branch --show-current`
   - Get git commit: `git rev-parse --short HEAD`
   - Get current task: Infer from recent conversation or ask user
   - Get current version: Parse from code or PROJECT_STATUS.md

3. **Read Preservation Template**:
   - Read `.claude/templates/PRESERVATION_PROMPT.md`
   - Substitute variables:
     - `{SESSION_NUMBER}` = session number from counter
     - `{TIMESTAMP}` = current timestamp
     - `{CURRENT_TASK}` = inferred or user-provided task description
     - `{GIT_BRANCH}` = current git branch
     - `{GIT_COMMIT}` = current git commit hash
     - `{TODOS_JSON}` = current TodoWrite state as JSON
     - `{IS_MILESTONE}` = true if --milestone flag present, false otherwise

4. **Invoke Context-Manager Agent**:
   - Use Task tool with `subagent_type: "context-management:context-manager"`
   - Pass the filled preservation template as the prompt
   - If IS_MILESTONE=true, add milestone instructions (see below)
   - Let agent analyze conversation and create structured files

5. **Increment Session Counter**:
   - After context-manager completes, increment session counter
   - Write new value to `.claude/.session_counter`

6. **Report Results**:
   - Show user where context was saved
   - Display next session number
   - If milestone save, show PROJECT_STATUS.md update
   - Remind user to clear context if at 60%+

## Expected Output Structure

### Standard Save (`/save-context`)
The context-manager agent will create:
- **Session file**: `.claude/context/active/session_N_TIMESTAMP.md`
- **Pattern files**: `.claude/knowledge/patterns/*.md` (if new patterns discovered)
- **Investigation files**: `.claude/knowledge/investigations/*.md` (if investigations completed)
- **ADR updates**: `.claude/project/TECHNICAL_DECISIONS.md` (if decisions made)

### Milestone Save (`/save-context --milestone`)
Same as standard save, PLUS:
- **PROJECT_STATUS.md update**: Updates version, ADRs, pending work, timestamp

## Milestone Save Instructions

When `--milestone` flag is present, append this to the context-manager prompt:

```markdown
## MILESTONE SAVE: Update PROJECT_STATUS.md

**Additional Task**: Update `.claude/PROJECT_STATUS.md` with current state

1. **Read current PROJECT_STATUS.md**

2. **Update these sections**:
   - **Last Updated**: Change to {TIMESTAMP}
   - **Current Version**: Update to latest version (check code or ADRs)
   - **Status**: Update based on current state (Stable/In Development/Ready for Release)
   - **Recent Architectural Decisions**: Add last 2-3 ADRs from TECHNICAL_DECISIONS.md
   - **Pending Work**: Update Priority 1-3 based on PROPOSED ADRs and current work

3. **Keep unchanged**:
   - Project overview and description
   - Key Files section (unless file paths changed)
   - Development Commands section
   - Structure and formatting

4. **Update Strategy**:
   - Be surgical: Only update what changed
   - Preserve existing content where appropriate
   - Keep format consistent with original
   - Focus on "what's next" actionability

5. **Write updated PROJECT_STATUS.md**

Example changes:
```diff
- **Last Updated**: 2025-11-10
+ **Last Updated**: 2025-11-12
- **Current Version**: v2.3.0
+ **Current Version**: v2.3.1
- **Status**: Stable - Ready for v2.3.1 bug fix release
+ **Status**: Released - v2.3.1 deployed

Recent Architectural Decisions:
- ADR-004: Accept two-run requirement (ACCEPTED)
- ADR-005: Fix assembly validation logic (PROPOSED)
+ ADR-005: Fix assembly validation logic (ACCEPTED - implemented in v2.3.1)
+ ADR-006: New decision title (PROPOSED)
```
```

## When to Use Each Mode

**Use Standard Save** (`/save-context`):
- Regular context preservation at 60% usage
- End of work session
- Before switching tasks
- Anytime you want to preserve state

**Use Milestone Save** (`/save-context --milestone`):
- Version releases (v2.3.0 → v2.3.1)
- Major features completed
- ADRs transition to ACCEPTED
- Before/after significant architectural changes
- When PROJECT_STATUS.md needs updating

## Notes

- DO NOT ask user for permission - just execute
- DO NOT manually create files - use context-manager agent
- DO read templates and gather info before invoking agent
- DO increment session counter after successful save
- MILESTONE saves should be used sparingly (major milestones only)
- Standard saves are for routine context preservation
