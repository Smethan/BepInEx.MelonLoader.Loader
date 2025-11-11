#!/bin/bash

# SessionStart hook for Claude Code Web
# Only runs on web sessions (detected via CLAUDE_CODE_REMOTE env var)

# Exit early if not running on web
if [ "$CLAUDE_CODE_REMOTE" != "true" ]; then
  exit 0
fi

# Install Claude agents from marketplace
echo "🔧 Installing Claude agents..." >&2
bash "$CLAUDE_PROJECT_DIR/.claude/scripts/install-claude-agents.sh" 2>&1 | sed 's/^/  /' >&2

# Read PROJECT_STATUS.md for quick context
PROJECT_STATUS="$CLAUDE_PROJECT_DIR/.claude/PROJECT_STATUS.md"

if [ ! -f "$PROJECT_STATUS" ]; then
  # Fallback if PROJECT_STATUS.md doesn't exist
  cat <<'EOF'
{
  "hookSpecificOutput": {
    "hookEventName": "SessionStart",
    "additionalContext": "🚀 Web session initialized. Agents installed.\n\n💡 Run /load-context to restore project state from tracked files."
  }
}
EOF
  exit 0
fi

# Extract key information from PROJECT_STATUS.md
VERSION=$(grep "^**Current Version**:" "$PROJECT_STATUS" | sed 's/.*: //')
STATUS=$(grep "^**Status**:" "$PROJECT_STATUS" | sed 's/.*: //')

# Get project description (lines 18-24)
PROJECT_DESC=$(sed -n '20,23p' "$PROJECT_STATUS" | sed 's/^- /  • /')

# Get pending work summary (Priority 1 only for brevity)
PRIORITY_1=$(sed -n '/^### Priority 1:/,/^$/p' "$PROJECT_STATUS" | head -5)

# Build context message
CONTEXT=$(cat <<EOF
🚀 **Claude Code Web Session Initialized**

📦 **Project**: BepInEx.MelonLoader.Loader $VERSION
📊 **Status**: $STATUS

**What this does**:
$PROJECT_DESC

**Next Actions**:
$PRIORITY_1

💡 **Tip**: Run \`/load-context\` for full project state restoration from ADRs and investigations.
📚 **Docs**: See \`.claude/CLAUDE.md\` for workflow instructions and context management.
EOF
)

# Output JSON with additionalContext
# Escape the context string properly for JSON
ESCAPED_CONTEXT=$(echo "$CONTEXT" | python3 -c 'import json, sys; print(json.dumps(sys.stdin.read()))')

cat <<EOF
{
  "hookSpecificOutput": {
    "hookEventName": "SessionStart",
    "additionalContext": $ESCAPED_CONTEXT
  }
}
EOF
