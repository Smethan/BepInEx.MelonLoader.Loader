# Context Management Guide

Quick reference for using the context management system effectively.

---

## Commands

### Save Context

**Standard Save** (routine context preservation):
```bash
/save-context
```

**What it does**:
- Creates session file in `.claude/context/active/`
- Updates `TECHNICAL_DECISIONS.md` with new ADRs
- Creates investigation files if work completed
- Increments session counter

**When to use**:
- Context usage reaches 60%+
- End of work session
- Before switching tasks
- Anytime you want to preserve state

---

**Milestone Save** (major updates):
```bash
/save-context --milestone
```

**What it does**:
- Everything from standard save, PLUS:
- Updates `.claude/PROJECT_STATUS.md` with:
  - Current version number
  - Recent ADRs (last 3-4)
  - Updated pending work
  - New timestamp

**When to use**:
- Version releases (v2.3.0 → v2.3.1)
- Major features completed
- ADRs transition to ACCEPTED
- Significant architectural changes
- When PROJECT_STATUS.md is stale

---

### Load Context

**Standard Load** (with session history):
```bash
/load-context
```

**What it does**:
- Finds most recent session file
- Loads full context from that session
- Displays summary of where you left off

**Use when**: Starting new session with prior work

---

**Load Specific Session**:
```bash
/load-context 5
```

**What it does**:
- Loads session #5 specifically
- Useful for going back to earlier state

---

**Fallback Mode** (no session files):
```bash
/load-context
```

**What it does** (when session files don't exist):
- Reads `.claude/PROJECT_STATUS.md`
- Reads `TECHNICAL_DECISIONS.md` for recent ADRs
- Displays current project state
- Shows pending work and next actions

**Use when**:
- Claude Code Web (fresh clone)
- Session files gitignored/missing
- Quick project orientation needed

---

## File Structure

### Tracked in Git (Persistent)
```
.claude/
├── CLAUDE.md                    # Main project instructions
├── PROJECT_STATUS.md            # Quick project overview (UPDATE WITH --milestone)
├── project/
│   ├── TECHNICAL_DECISIONS.md  # All ADRs (auto-updated)
│   ├── ARCHITECTURE.md          # System design
│   └── CONSTRAINTS.md           # Technical constraints
├── knowledge/
│   ├── patterns/                # Reusable patterns
│   └── investigations/          # Completed investigations
├── templates/                   # Command templates
├── commands/                    # Slash command definitions
└── versions/                    # Version documentation
```

### Not Tracked (Ephemeral)
```
.claude/
├── context/
│   └── active/
│       └── session_*.md        # Session snapshots (local only)
└── .session_counter            # Current session number
```

---

## Typical Workflow

### Daily Work Session

1. **Start**: Run `/load-context`
   - Restores where you left off
   - Shows pending work

2. **Work**: Make changes, track with TodoWrite
   - Code changes
   - Architectural decisions
   - Investigations

3. **Save** (at 60% context): Run `/save-context`
   - Preserves session state
   - Updates ADRs if decisions made

4. **Clear**: User clears context (Ctrl+L)

5. **Resume**: Next session starts with `/load-context`

---

### Release Workflow

1. **Implement feature**

2. **Test and validate**

3. **Update ADRs**: Mark as ACCEPTED
   - Use Edit tool on `TECHNICAL_DECISIONS.md`
   - Change status: PROPOSED → ACCEPTED

4. **Milestone save**: Run `/save-context --milestone`
   - Updates PROJECT_STATUS.md automatically
   - Records version change
   - Updates pending work

5. **Commit changes**:
   ```bash
   git add .claude/PROJECT_STATUS.md
   git add .claude/project/TECHNICAL_DECISIONS.md
   git commit -m "Release v2.3.1"
   ```

---

## Examples

### Example 1: Regular Work Session
```
User: [works on validation bug fix]
Claude: [implements fix at 50% context]

User: /save-context
Claude: ✅ Context preserved
        - Session 3 saved
        - ADR-005 updated (PROPOSED)
        - Next session: 4

User: [clears context]
User: /load-context
Claude: ✅ Session 3 restored
        - Working on: Validation bug fix
        - File: InteropRedirectorPatcher.cs:756
        - Next: Test the fix
```

### Example 2: Version Release
```
User: [completes v2.3.1 validation fix]
User: [tests successfully]
User: [updates ADR-005 status to ACCEPTED]

User: /save-context --milestone
Claude: ✅ Context preserved (MILESTONE)
        - Session 4 saved
        - ADR-005 marked ACCEPTED
        - PROJECT_STATUS.md updated:
          * Version: v2.3.0 → v2.3.1
          * Status: Ready → Released
          * ADR-005: PROPOSED → ACCEPTED
        - Next session: 5

User: [commits to git]
```

### Example 3: Claude Code Web (Fresh Clone)
```
User: /load-context
Claude: 📋 No session files found - using fallback mode

        PROJECT STATE (from tracked files):

        Version: v2.3.1
        Status: Released

        Recent Decisions:
        - ADR-005: Fix validation logic (ACCEPTED - v2.3.1)
        - ADR-004: Accept two-run requirement (ACCEPTED)

        Pending Work:
        - [ ] None - v2.3.1 complete

        Next Actions:
        1. Review PROJECT_STATUS.md for details
        2. Check logs if issues reported
```

---

## Best Practices

### When to Use Standard Save
✅ **DO** use for:
- Regular context preservation (60%+ usage)
- End of work session
- Before task switching
- Routine state preservation

❌ **DON'T** use for:
- After every small change
- When context < 40%
- Multiple times in one session (unless context high)

### When to Use Milestone Save
✅ **DO** use for:
- Version releases
- Major feature completion
- ADRs becoming ACCEPTED
- Architectural changes

❌ **DON'T** use for:
- Regular work sessions
- Minor changes
- Experimental work
- Multiple times per day

### PROJECT_STATUS.md Updates
- **Milestone saves**: Automatic via `--milestone` flag
- **Manual updates**: For non-milestone changes (rare)
- **Frequency**: Only at significant milestones
- **Purpose**: Keep entry point fresh for new Claude instances

---

## Troubleshooting

**Q: Context files not found on Claude Code Web?**
A: Normal! Session files are gitignored. Use `/load-context` fallback mode or read PROJECT_STATUS.md directly.

**Q: When should I manually update PROJECT_STATUS.md?**
A: Rarely. Use `/save-context --milestone` instead. Manual updates only for non-milestone changes that need documenting.

**Q: How do I know if PROJECT_STATUS.md is stale?**
A: Check "Last Updated" timestamp. If > 1 week old and work happened, consider milestone save.

**Q: What if I forget to use --milestone?**
A: No problem! Manually update PROJECT_STATUS.md later, or use `--milestone` on next save.

**Q: Can I use both modes in one session?**
A: Yes, but typically you'd only use milestone save once at the end when releasing/completing major work.

---

## Summary

**Two-Tier System**:
1. **Session files** (ephemeral) - Detailed conversation history
2. **Tracked files** (persistent) - Curated project knowledge

**Two Save Modes**:
1. **Standard** - Routine preservation (session + ADRs)
2. **Milestone** - Major updates (also updates PROJECT_STATUS.md)

**Fallback Support**:
- Claude Code Web automatically uses tracked files when session files missing
- PROJECT_STATUS.md serves as entry point for fresh instances
- No manual intervention needed

**Key Insight**:
Session history is nice to have, but project state lives in tracked files (ADRs, PROJECT_STATUS.md). This system works everywhere!
