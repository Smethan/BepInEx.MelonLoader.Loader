# Load Context Command

Restore session state from a previous context preservation using the context-manager agent.

## Instructions

You MUST use the context-management:context-manager agent to restore context. Follow these steps:

1. **Find Session File**:
   - Check for session files in `.claude/context/active/`
   - If user provided session number (e.g., "/load-context 5"), find that session
   - Otherwise, find most recent session file: `ls -t .claude/context/active/session_*.md | head -1`
   - If no files found, **use FALLBACK mode** (see below)

2. **Read Restoration Template**:
   - Read `.claude/templates/RESTORATION_PROMPT.md`
   - Substitute `{SESSION_FILE}` with the found session file path

3. **Invoke Context-Manager Agent**:
   - Use Task tool with `subagent_type: "context-management:context-manager"`
   - Pass the filled restoration template as the prompt
   - Let agent read session file and related knowledge files

4. **Display Restoration Summary**:
   - Show user what was being worked on
   - Show what's pending
   - Show next actions
   - Show key file locations and line numbers
   - Keep summary CONCISE (under 30 lines)

5. **Check Current State**:
   - After restoration, verify git status matches expectations
   - Check if any uncommitted changes exist
   - Briefly mention if state differs from saved session

## Usage Examples

**Load most recent session**:
```
/load-context
```

**Load specific session number**:
```
/load-context 5
```

## Expected Behavior

The context-manager agent will:
1. Read the session file
2. Read related knowledge files (patterns, investigations, ADRs)
3. Check current git status
4. Provide a concise restoration summary
5. List immediate next actions

## Output Format

The restoration summary should include:
- **Task**: What was being worked on
- **Progress**: What was completed
- **Pending**: What remains to be done
- **Files**: Key files and line numbers
- **Next Actions**: Specific steps to continue (1-3 items)

## FALLBACK Mode: No Session Files Available

When no session files exist (e.g., Claude Code Web with gitignored context folder):

1. **Read Tracked Project State Files** (in order):
   - `.claude/PROJECT_STATUS.md` - Quick project overview and pending work
   - `.claude/project/TECHNICAL_DECISIONS.md` - Recent ADRs and decisions
   - `.claude/project/ARCHITECTURE.md` - System design (if exists)
   - `.claude/project/CONSTRAINTS.md` - Technical constraints (if exists)
   - `.claude/knowledge/investigations/*.md` - Recent investigations
   - `.claude/versions/v{latest}/` - Latest version docs (if exists)

2. **Invoke Context-Manager with Fallback Prompt**:
   ```
   No session files found. Using fallback mode to restore project state.

   Read these tracked files to understand current project state:
   1. .claude/project/TECHNICAL_DECISIONS.md (focus on most recent ADRs)
   2. .claude/project/ARCHITECTURE.md (if exists)
   3. .claude/knowledge/investigations/*.md (recent work)

   Provide a project state summary:
   - Current version and status
   - Recent architectural decisions (last 2-3 ADRs)
   - Pending work (check ADRs with "PROPOSED" status)
   - Next recommended actions
   - Key file locations

   Format: Concise, actionable, focused on "what to do next"
   ```

3. **Display Fallback Summary**:
   - Show most recent ADRs and their status
   - Show pending work from PROPOSED ADRs
   - Show key file locations
   - Recommend next actions based on ADR priorities

**Fallback Summary Format**:
```
📋 Project State (from tracked files - no session history)

Version: v{VERSION}
Status: {STATUS}

Recent Decisions:
- ADR-{N}: {TITLE} - {STATUS}
- ADR-{N+1}: {TITLE} - {STATUS}

Pending Work:
- [ ] {ITEM_FROM_PROPOSED_ADR}
- [ ] {ITEM_FROM_PROPOSED_ADR}

Key Files:
- {FILE}:{LINES} - {DESCRIPTION}

Next Actions:
1. {SPECIFIC_ACTION}
2. {SPECIFIC_ACTION}
```

## Notes

- DO NOT ask user for permission - just execute
- DO keep summary concise and actionable
- DO include file paths and line numbers
- DO check if git state matches saved session
- DO list specific next steps (not vague suggestions)
- FALLBACK mode is for Claude Code Web or fresh clones without session history
- FALLBACK focuses on ADRs since they capture actionable decisions
