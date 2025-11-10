# Context Management System

A prompt-driven context preservation and restoration system using Claude's context-manager agent.

## Quick Start

### Save Context (when at 60% usage)

**Option 1: Direct (simplest)**
```bash
# Just tell Claude:
"Save context - I'm implementing Harmony patch fixes"
```

Claude will:
1. Read `.claude/templates/PRESERVATION_PROMPT.md`
2. Invoke context-manager agent with current session info
3. Save to `.claude/context/active/session_N_TIMESTAMP.md`
4. Promote completed knowledge to `.claude/knowledge/` if applicable

**Option 2: Using helper script**
```bash
./claude/scripts/save-context.sh "description of current task"
# Displays the prompt to use with context-manager
```

### Restore Context (new session)

**Option 1: Direct (simplest)**
```bash
# Just tell Claude:
"Load context from most recent session"
```

Claude will:
1. Find latest session file in `.claude/context/active/`
2. Read `.claude/templates/RESTORATION_PROMPT.md`
3. Invoke context-manager agent to restore context
4. Display summary of where you left off

**Option 2: Using helper script**
```bash
./claude/scripts/load-context.sh [session_number]
# Shows restoration prompt and preview
```

---

## Directory Structure

```
.claude/
├── CLAUDE.md                      # ✅ TRACKED - Agent instructions
├── README.md                      # ✅ TRACKED - This file
│
├── project/                       # ✅ TRACKED - Core project knowledge
│   ├── ARCHITECTURE.md            # System design
│   ├── TECHNICAL_DECISIONS.md     # Architecture Decision Records (ADRs)
│   └── CONSTRAINTS.md             # Technical constraints
│
├── versions/                      # ✅ TRACKED - Version documentation
│   ├── v2.3.0/
│   │   ├── RELEASE_NOTES.md
│   │   └── IMPLEMENTATION.md
│   └── CURRENT → v2.3.0/          # Symlink to current version
│
├── knowledge/                     # ✅ TRACKED - Persistent knowledge base
│   ├── patterns/                  # Reusable code patterns
│   │   ├── harmony_patching.md
│   │   └── assembly_resolution.md
│   └── investigations/            # Completed root cause analyses
│       └── net6_validation_bypass.md
│
├── templates/                     # ✅ TRACKED - Prompt templates
│   ├── PRESERVATION_PROMPT.md     # Context preservation instructions
│   └── RESTORATION_PROMPT.md      # Context restoration instructions
│
├── scripts/                       # ✅ TRACKED - Helper utilities
│   ├── save-context.sh            # Build preservation prompt
│   └── load-context.sh            # Build restoration prompt
│
├── context/                       # ❌ IGNORED - Session state (local only)
│   ├── active/                    # Current session files
│   │   └── session_N_TIMESTAMP.md
│   └── archive/                   # Old sessions (manual cleanup)
│
└── .session_counter               # ❌ IGNORED - Auto-incrementing counter
```

**Key Principle**:
- **TRACKED** (git) = Core knowledge, patterns, decisions, templates
- **IGNORED** (local) = Ephemeral session state

---

## How It Works

### Preservation Flow

1. **Trigger**: When context reaches 60%, tell Claude to save context
2. **Template**: Claude reads `.claude/templates/PRESERVATION_PROMPT.md`
3. **Invoke**: Claude uses Task tool with context-manager agent
4. **Agent Work**:
   - Analyzes conversation
   - Captures current task, decisions, code locations
   - Saves to `.claude/context/active/session_N_TIMESTAMP.md`
   - Promotes completed knowledge to `.claude/knowledge/` if applicable
5. **Result**: Structured session file with RESTORATION_INSTRUCTIONS

### Restoration Flow

1. **Trigger**: New session starts, tell Claude to load context
2. **Template**: Claude reads `.claude/templates/RESTORATION_PROMPT.md`
3. **Invoke**: Claude uses Task tool with context-manager agent
4. **Agent Work**:
   - Reads latest session file
   - Extracts RESTORATION_INSTRUCTIONS
   - Checks related knowledge files
   - Provides actionable summary
5. **Result**: Claude knows exactly where you left off

---

## Workflow Examples

### Daily Development Workflow

**Morning (start work):**
```bash
# Pull latest knowledge from git
git pull

# Tell Claude:
"Load context from most recent session"

# Claude restores context and you continue working
```

**During Work (context at 60%):**
```bash
# Tell Claude:
"Save context - implementing assembly validation fixes"

# Claude preserves state
# You clear conversation (Ctrl+L)
# Tell Claude: "Load context from most recent session"
# Continue working
```

**Evening (end work):**
```bash
# Tell Claude:
"Save context - session ending"

# Commit any code changes
git add .
git commit -m "feat: implement assembly validation bypass"

# Commit any new knowledge (if Claude created patterns/investigations)
git add .claude/
git commit -m "docs: add harmony patching pattern"
git push
```

### Cross-Machine Sync

**Machine A (laptop):**
```bash
# End work session
"Save context - switching to desktop"

# Push knowledge to GitHub
git push
```

**Machine B (desktop):**
```bash
# Pull latest
git pull

# Restore context
"Load context from most recent session"

# Continue exactly where you left off
```

---

## Template Customization

### Preservation Template

Edit `.claude/templates/PRESERVATION_PROMPT.md` to customize:
- What information to capture
- File naming conventions
- Knowledge promotion criteria
- Output format

### Restoration Template

Edit `.claude/templates/RESTORATION_PROMPT.md` to customize:
- Summary format
- Context depth
- Related knowledge inclusion
- Quick reference format

---

## File Formats

### Session File Structure

```markdown
# Session N - TIMESTAMP

## Current Task
[What's being worked on]

## Recent Changes
[Files modified]

## Work Completed
[Accomplishments]

## Pending Work
[What remains]

## Technical Context
### Architectural Decisions Made
[Decisions and rationale]

### Code Patterns Used
[Patterns established]

### Rejected Alternatives
[What didn't work and why]

## RESTORATION_INSTRUCTIONS
### Quick Start (5 minutes)
1. [First step]
2. [Second step]
3. [Where to continue]

### Detailed Context (15 minutes)
[More thorough restoration]

### Success Criteria
- [ ] [Criterion 1]
- [ ] [Criterion 2]

## Code References
- `file.cs:123-145` - Description
- `another.cs:67` - Important location

## Metadata
- Session number: N
- Duration: ~45 minutes
- Git branch: master
- Git commit: abc123
```

---

## Knowledge Promotion

When context-manager preserves context, it may promote ephemeral session knowledge to tracked locations:

### Pattern Discovery
If a reusable pattern emerged:
- **From**: Session notes
- **To**: `.claude/knowledge/patterns/{pattern_name}.md`
- **Action**: Commit to git

### Investigation Complete
If a bug investigation finished:
- **From**: Session investigation notes
- **To**: `.claude/knowledge/investigations/{topic}.md`
- **Action**: Commit to git

### Architectural Decision
If a significant decision was made:
- **From**: Session decisions
- **To**: `.claude/project/TECHNICAL_DECISIONS.md` (append)
- **Action**: Commit to git

---

## Git Workflow

### What Gets Committed

**Always commit**:
- `.claude/project/` - Architecture and decisions
- `.claude/versions/` - Version documentation
- `.claude/knowledge/` - Patterns and investigations
- `.claude/templates/` - Prompt templates
- `.claude/scripts/` - Helper scripts

**Never commit**:
- `.claude/context/active/` - Current sessions (local only)
- `.claude/context/archive/` - Old sessions
- `.claude/.session_counter` - Session numbering

### Commit Messages

When committing context files:
```bash
# Knowledge added
git commit -m "docs: add harmony patching pattern"

# Investigation completed
git commit -m "docs: document .NET 6 validation bypass investigation"

# Architecture decision
git commit -m "docs: ADR for using Harmony over custom ALC"

# Multiple updates
git commit -m "docs: update context for v2.3.0 release"
```

---

## Troubleshooting

### "No session files found"
```bash
# Check session counter
cat .claude/.session_counter

# List active sessions
ls -la .claude/context/active/

# If empty, this is first session - no context to restore
```

### "Template not found"
```bash
# Verify templates exist
ls -la .claude/templates/

# Should see:
# - PRESERVATION_PROMPT.md
# - RESTORATION_PROMPT.md
```

### Context-manager not creating files
- Check that `.claude/context/active/` directory exists
- Verify Claude has write permissions
- Ensure prompt includes file path instructions

### Knowledge not syncing across machines
- Verify `.claude/knowledge/` is NOT in .gitignore
- Check git status: `git status .claude/`
- Commit and push: `git add .claude/ && git commit -m "docs: update context"`

---

## Advanced Usage

### Custom Session Numbers
```bash
# Load specific session
./claude/scripts/load-context.sh 5
```

### Manual Session File Creation
```bash
# Create session file manually
cat > .claude/context/active/session_99_2025-11-09_120000.md << 'EOF'
# Session 99 - Manual Entry

## Current Task
Testing manual context creation

## RESTORATION_INSTRUCTIONS
1. This is a manually created session
2. Continue with normal workflow
EOF
```

### Archive Old Sessions
```bash
# Move old sessions to archive
mv .claude/context/active/session_[1-5]_*.md .claude/context/archive/
```

---

## Integration with CLAUDE.md

The context management system integrates with `.claude/CLAUDE.md`:

**At 60% context usage**, Claude will:
1. Announce: "⚠️ Context at 60%, activating context-manager..."
2. Read preservation template
3. Invoke context-manager agent
4. Save session state
5. Request context clear
6. On new session, automatically restore context

**Automatic behavior** - no user intervention needed (but user can trigger manually too).

---

## Session Counter

- **Location**: `.claude/.session_counter`
- **Format**: Single integer (current session number)
- **Auto-increment**: Helper scripts increment after each save
- **Reset**: Manually edit file if needed (not recommended)

---

## FAQ

**Q: Why not track session files in git?**
A: Too noisy - creates merge conflicts and clutters history. Only completed knowledge is tracked.

**Q: What if I lose local session files?**
A: Not catastrophic - core knowledge is in git. Session files are just for convenience.

**Q: Can I customize the prompt templates?**
A: Yes! Edit `.claude/templates/*.md` files. They're version-controlled.

**Q: How do I know what session I'm on?**
A: `cat .claude/.session_counter` or check latest session file timestamp.

**Q: Can I use this without helper scripts?**
A: Yes! Just tell Claude "save context" or "load context" directly. Scripts are optional.

---

## File Locations Reference

| File | Purpose | Tracked |
|------|---------|---------|
| `CLAUDE.md` | Agent instructions | ✅ Yes |
| `README.md` | This documentation | ✅ Yes |
| `project/ARCHITECTURE.md` | System design | ✅ Yes |
| `project/TECHNICAL_DECISIONS.md` | ADRs | ✅ Yes |
| `versions/v2.3.0/RELEASE_NOTES.md` | Release notes | ✅ Yes |
| `knowledge/patterns/*.md` | Code patterns | ✅ Yes |
| `knowledge/investigations/*.md` | Investigations | ✅ Yes |
| `templates/PRESERVATION_PROMPT.md` | Save template | ✅ Yes |
| `templates/RESTORATION_PROMPT.md` | Load template | ✅ Yes |
| `scripts/save-context.sh` | Helper script | ✅ Yes |
| `scripts/load-context.sh` | Helper script | ✅ Yes |
| `context/active/session_*.md` | Session state | ❌ No |
| `.session_counter` | Session number | ❌ No |

---

## Next Steps

1. **Read** `.claude/templates/PRESERVATION_PROMPT.md` to understand preservation
2. **Read** `.claude/templates/RESTORATION_PROMPT.md` to understand restoration
3. **Try it**: Tell Claude "save context" and observe the result
4. **Customize**: Edit templates to match your workflow
5. **Commit**: Push knowledge files to GitHub for cross-machine sync

---

**Last Updated**: 2025-11-09
**Version**: 1.0
**Status**: Production Ready
