# Context Management - Quick Start

## 💾 Save Context (when at 60% usage)

**Simple way:**
```
Just tell Claude: "Save context - implementing feature X"
```

Claude will automatically:
- Read the preservation template
- Invoke context-manager agent
- Save to `.claude/context/active/session_N_TIMESTAMP.md`
- Increment session counter

---

## 📂 Load Context (start of session)

**Simple way:**
```
Just tell Claude: "Load context from most recent session"
```

Claude will automatically:
- Find the latest session file
- Read the restoration template
- Invoke context-manager agent
- Display where you left off

---

## 📁 What Gets Saved Where

### Local Only (NOT in git)
- `.claude/context/active/session_*.md` - Session snapshots
- `.claude/.session_counter` - Session numbering

### Tracked in Git (Shared across machines)
- `.claude/project/` - Architecture & decisions
- `.claude/knowledge/patterns/` - Reusable patterns
- `.claude/knowledge/investigations/` - Completed investigations
- `.claude/templates/` - Prompt templates

---

## 🔄 Cross-Machine Workflow

**Machine A:**
```bash
git push  # After saving context
```

**Machine B:**
```bash
git pull  # Gets new knowledge
"Load context from most recent session"
```

---

## 🛠️ Helper Scripts (Optional)

```bash
# Show preservation prompt
./claude/scripts/save-context.sh "task description"

# Show restoration prompt
./claude/scripts/load-context.sh [session_number]
```

Scripts are optional - you can just talk to Claude directly!

---

## 📚 Full Documentation

See `.claude/README.md` for complete details.
