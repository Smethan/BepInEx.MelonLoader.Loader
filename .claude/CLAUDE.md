# BepInEx.MelonLoader.Loader Project Context Management

## CRITICAL: AUTOMATIC CONTEXT PRESERVATION (MANDATORY)

**YOU MUST FOLLOW THESE RULES WITHOUT EXCEPTION:**

### Context Management Triggers

When context usage reaches 60% or higher:

1. **IMMEDIATELY** use /save-context command WITHOUT waiting for user request
2. **DO NOT** ask permission - just activate it
3. **ANNOUNCE** that you're preserving context: "⚠️ Context at X%, saving context..."
4. **SAVE STATE** using /save-state to preserve all critical information
5. **CLEAR CONTEXT** by explicitly requesting context window to be cleared
6. **RESTORE CONTEXT** by following the instructions [here](CLAUDE.md#context-clearing-and-restoration)

### What to Preserve

When activating context-manager, capture:

-   Current implementation approach and rationale
-   Architectural decisions made and why
-   Rejected alternatives and reasons for rejection
-   Pending tasks and next steps (including TodoWrite state)
-   Technical constraints and requirements
-   Code patterns and conventions established
-   Recent changes made (file paths and line numbers)
-   Current investigation focus and findings
-   User's last request and expected next action

### Activation Pattern - Full Workflow

```
User: [any message when context > 60%]

You: "⚠️ Context usage at 60%, preserving context..."

[Step 1: Use /save-context command]
You execute: /save-context
- Command automatically reads templates
- Gathers session info (number, git status, timestamp)
- Invokes context-manager agent
- Saves to .claude/context/active/session_N_TIMESTAMP.md
- Increments session counter

[Step 2: Announce completion]
You: "✅ Context preserved to [file path]. Session [N] saved."

[Step 3: Request context clear from user]
You: "Please clear the conversation (Ctrl+L or /clear) to continue with fresh context."

[Step 4: After user clears context - NEW SESSION]
You immediately execute: /load-context
- Command finds most recent session file
- Invokes context-manager for restoration
- Displays concise summary of where we left off

You: "✅ Context restored from session [N]. Continuing: [brief task summary]"
[Continue with user's last request]
```

### User-Initiated Context Management

Users can also manually trigger context management:

**Save current state**:

```
User: /save-context
Claude: [Preserves context and increments session counter]
```

**Restore previous session** (most recent):

```
User: /load-context
Claude: [Loads and summarizes most recent session]
```

**Restore specific session**:

```
User: /load-context 5
Claude: [Loads and summarizes session 5]
```

### Context Clearing and Restoration

**When to Clear:**

-   Immediately after `/save-context` completes at 60%
-   Before hitting 80% (emergency threshold)
-   When instructed by previous session

**How to Restore:**

1. Execute `/load-context` at start of new session
2. Command automatically finds most recent session file
3. Context-manager provides concise restoration summary
4. Continue with the user's last request
5. Use context-manager's output to guide next actions

**Session File Location:**

-   Session files saved to: `.claude/context/active/session_N_TIMESTAMP.md`
-   Session counter tracked in: `.claude/.session_counter`
-   Always check for most recent session on startup
-   Include "RESTORATION_INSTRUCTIONS" section in saved files

**Automation Benefits:**

-   ✅ No manual script execution required
-   ✅ No copy-paste of prompts
-   ✅ Automatic session tracking
-   ✅ Consistent structure across saves

### When to Activate

-   Context exceeds 60% ✓
-   Before any compaction warning ✓
-   End of complex implementation session ✓
-   After major architectural decisions ✓
-   When switching between major tasks ✓

**DO NOT:**

-   Wait for user to request context preservation
-   Skip context management "to be helpful"
-   Assume you'll remember context after compaction
-   Continue working past 60% without saving state
-   Save state without providing clear restoration instructions
-   Fail to request context clear after preservation

## Project Technical Context

### Core Technologies

-   .NET 6+ (prefer modern approaches over legacy)
-   C# systems programming (AssemblyLoadContext, IL2CPP interop)
-   BepInEx + MelonLoader framework integration
-   Nuke build automation

### Key Constraints

-   BepInEx and MelonLoader must coexist
-   Support both r2modman and manual installations
-   Assembly resolution must handle first-run scenarios
-   Prefer .NET 6+ APIs (AssemblyLoadContext over AppDomain)

# Locations and Dev environment info

## Local Development

-   Log files for BepInEx can be found at [this](file:/mnt/c/Users/Ethan/AppData/Roaming/r2modmanPlus-local/Megabonk/profiles/Default/BepInEx/LogOutput.log) location
-   When doing a final build (build for the user to test) use `./build.sh DevDeploy`
-   Ignore MelonLoader-upstream when reviewing and building, only reference it when investigating bugs in melonloader itself, not our mod

## Remote Development

-   Log files can be provided by user, ask if needed
-   For a final build, use `./build.sh`

### CRITICAL: AGENT ORCHESTRATION

-   **ALWAYS** use coordinator for multi-step problems
-   **ANNOUNCE** that you are using coordinator: "Using coordinator to orchestrate X"
-   **DO NOT** ask for permission, just use it when appropriate

### CRITICAL: PULL REQUEST WORKFLOW

When creating pull requests, **ALWAYS** follow this workflow:

1.  **FIRST**: Run `/save-context --milestone` to preserve context
2.  **WAIT**: Let milestone save complete successfully
3.  **THEN**: Create the PR with `gh pr create`
4.  **REPORT**: Confirm milestone saved and show PR URL

**Why**: Every PR should have associated milestone documentation tracking the architectural decisions, version changes, and project state at that point in time.

**Shortcut**: Use `/create-pr` command which handles the entire workflow automatically.
