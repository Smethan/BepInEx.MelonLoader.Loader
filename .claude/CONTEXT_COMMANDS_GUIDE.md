# Context Management Commands - Quick Reference

## Overview

Context preservation and restoration is now fully automated using slash commands. No more manual script execution or copy-paste!

## Available Commands

### `/save-context`

**Purpose**: Preserve current session state

**Usage**:
```
/save-context
```

**What it does**:
1. Reads preservation template from `.claude/templates/PRESERVATION_PROMPT.md`
2. Gathers current session info (number, git status, timestamp)
3. Invokes context-manager agent to analyze conversation
4. Creates session file: `.claude/context/active/session_N_TIMESTAMP.md`
5. Optionally creates knowledge files (patterns, investigations, ADRs)
6. Increments session counter

**When to use**:
- Context usage reaches 60%+
- End of complex implementation session
- After major architectural decisions
- Before switching to different tasks
- Manual context preservation (any time)

**Output**:
- Session file location
- Next session number
- List of any knowledge files created

---

### `/load-context`

**Purpose**: Restore previous session state

**Usage**:
```
/load-context              # Load most recent session
/load-context 5            # Load specific session number
```

**What it does**:
1. Finds session file (most recent or specified number)
2. Reads restoration template from `.claude/templates/RESTORATION_PROMPT.md`
3. Invokes context-manager agent to analyze session file
4. Provides concise restoration summary
5. Lists immediate next actions

**When to use**:
- Start of new session after `/clear`
- After restarting Claude Code
- When switching back to this project
- When you need to recall what was being worked on

**Output**:
- Task summary (what was being worked on)
- Progress made (what was completed)
- Pending work (what remains)
- Key file locations (with line numbers)
- Next actions (specific steps to continue)

---

## Typical Workflow

### Automatic Context Preservation (at 60%)

```
[Context reaches 60%]

Claude: "⚠️ Context usage at 60%, preserving context..."
        /save-context

Claude: "✅ Context preserved to .claude/context/active/session_7_2025-11-10_143022.md
        Session 7 saved. Next session: 8"

        "Please clear the conversation (Ctrl+L or /clear) to continue with fresh context."

[User presses Ctrl+L to clear]

[New session starts]

Claude: /load-context
        "✅ Context restored from session 7. Continuing: Implementing Harmony patch..."

        [Provides summary and continues work]
```

### Manual Context Save/Load

```
User: /save-context

Claude: [Preserves context, increments counter]
        "✅ Context saved to session 5"

[Later...]

User: /load-context

Claude: [Loads and summarizes session 5]
        "Restored session 5: You were implementing..."
```

---

## File Structure

### Session Files (Ephemeral - Not tracked in git)
**Location**: `.claude/context/active/`

Files:
- `session_N_TIMESTAMP.md` - Complete session snapshots
- Automatically cleaned up when archived

### Knowledge Files (Tracked in git)
**Locations**:
- `.claude/knowledge/patterns/` - Reusable patterns
- `.claude/knowledge/investigations/` - Completed investigations
- `.claude/project/TECHNICAL_DECISIONS.md` - Architecture Decision Records

Created by context-manager when:
- New reusable patterns emerge
- Investigations are completed
- Architectural decisions are made

### Session Counter
**Location**: `.claude/.session_counter`

Contains current session number, auto-incremented by `/save-context`

---

## Templates

The commands use structured templates to ensure consistency:

### Preservation Template
**File**: `.claude/templates/PRESERVATION_PROMPT.md`

**Variables**:
- `{SESSION_NUMBER}` - Current session from counter
- `{TIMESTAMP}` - Current date/time
- `{CURRENT_TASK}` - Inferred from conversation
- `{GIT_BRANCH}` - Current git branch
- `{GIT_COMMIT}` - Current git commit
- `{TODOS_JSON}` - TodoWrite state

### Restoration Template
**File**: `.claude/templates/RESTORATION_PROMPT.md`

**Variables**:
- `{SESSION_FILE}` - Path to session file to restore

---

## Benefits Over Old Scripts

| Old Way | New Way |
|---------|---------|
| Run `./save-context.sh` | Type `/save-context` |
| Copy-paste long prompt | Automatic |
| Manually invoke agent | Automatic |
| Check output for file path | Automatic |
| Run `./load-context.sh` | Type `/load-context` |
| Copy-paste restoration prompt | Automatic |
| Manually invoke agent | Automatic |

**Result**: One command instead of 4+ manual steps!

---

## Advanced Usage

### Checking Available Sessions

```bash
ls -la .claude/context/active/
```

Shows all saved sessions with timestamps.

### Viewing Session Counter

```bash
cat .claude/.session_counter
```

Shows next session number.

### Manual Session File Reading

```bash
cat .claude/context/active/session_7_2025-11-10_143022.md
```

Read session file directly if needed.

### Archiving Old Sessions

Move old sessions to archive:
```bash
mkdir -p .claude/context/archive/$(date +%Y-%m-%d)
mv .claude/context/active/session_5_*.md .claude/context/archive/$(date +%Y-%m-%d)/
```

---

## Troubleshooting

### "No session files found"

**Problem**: `/load-context` can't find any sessions

**Solution**:
1. Check if session files exist: `ls .claude/context/active/`
2. If none, this is the first session - no need to restore
3. Previous sessions might be archived

### "Session number X not found"

**Problem**: Specific session doesn't exist

**Solution**:
1. List available sessions: `ls .claude/context/active/session_*.md`
2. Use correct session number or omit to load most recent

### Context-manager agent fails

**Problem**: Agent encounters error during save/load

**Solution**:
1. Check templates exist: `ls .claude/templates/`
2. Verify `.claude/context/active/` directory exists
3. Check git status (branch/commit needed for preservation)
4. Review error message from agent

### Session counter out of sync

**Problem**: Session numbers don't match expectations

**Solution**:
1. Check counter: `cat .claude/.session_counter`
2. Manually adjust if needed: `echo "10" > .claude/.session_counter`
3. Counter auto-increments on each `/save-context`

---

## Migration from Old Scripts

The old bash scripts (`save-context.sh`, `load-context.sh`) are now **deprecated** but remain available as fallbacks.

**Recommended**: Use slash commands exclusively for better UX.

**If you prefer scripts**: They still work, but output deprecation warnings.

---

## Quick Reference Card

```
┌─────────────────────────────────────────────────┐
│  Context Management Commands                    │
├─────────────────────────────────────────────────┤
│  Save:    /save-context                         │
│  Load:    /load-context [session_number]        │
│                                                  │
│  Files:   .claude/context/active/session_N_*.md │
│  Counter: .claude/.session_counter              │
│                                                  │
│  When:    60%+ context or end of session        │
│  Auto:    Claude triggers at 60%                │
└─────────────────────────────────────────────────┘
```

---

## See Also

- **CLAUDE.md** - Full project context management rules
- **RESTORATION_GUIDE.md** - Detailed restoration procedures
- **.claude/templates/** - Command templates
- **.claude/knowledge/** - Preserved patterns and investigations
