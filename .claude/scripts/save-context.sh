#!/bin/bash
# save-context.sh - Helper to invoke context-manager for context preservation
# Usage: ./save-context.sh "description of current task"
#
# ⚠️ DEPRECATED: This script is deprecated in favor of the /save-context slash command.
# Please use: /save-context
# This script remains available as a fallback for manual invocation.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
TEMPLATE_FILE="$PROJECT_ROOT/.claude/templates/PRESERVATION_PROMPT.md"
SESSION_COUNTER_FILE="$PROJECT_ROOT/.claude/.session_counter"

# Initialize or read session counter
if [ -f "$SESSION_COUNTER_FILE" ]; then
    SESSION_NUMBER=$(cat "$SESSION_COUNTER_FILE")
else
    SESSION_NUMBER=1
    mkdir -p "$(dirname "$SESSION_COUNTER_FILE")"
    echo "$SESSION_NUMBER" > "$SESSION_COUNTER_FILE"
fi

TIMESTAMP=$(date +%Y-%m-%d_%H%M%S)
CURRENT_TASK="${1:-In progress}"

# Gather current context
cd "$PROJECT_ROOT"
GIT_BRANCH=$(git branch --show-current 2>/dev/null || echo "unknown")
GIT_COMMIT=$(git rev-parse --short HEAD 2>/dev/null || echo "unknown")

# Read template
if [ ! -f "$TEMPLATE_FILE" ]; then
    echo "❌ Template not found: $TEMPLATE_FILE"
    exit 1
fi

# Build prompt by substituting variables
PROMPT=$(cat "$TEMPLATE_FILE" | \
    sed "s/{SESSION_NUMBER}/$SESSION_NUMBER/g" | \
    sed "s/{TIMESTAMP}/$TIMESTAMP/g" | \
    sed "s|{CURRENT_TASK}|$CURRENT_TASK|g" | \
    sed "s/{GIT_BRANCH}/$GIT_BRANCH/g" | \
    sed "s/{GIT_COMMIT}/$GIT_COMMIT/g")

# Output instructions for user
echo "=========================================="
echo "Context Preservation - Session $SESSION_NUMBER"
echo "=========================================="
echo ""
echo "Task: $CURRENT_TASK"
echo "Timestamp: $TIMESTAMP"
echo ""
echo "To save context, use the following prompt with context-manager agent:"
echo ""
echo "---"
echo "$PROMPT"
echo "---"
echo ""
echo "Or, if you're in Claude Code CLI, tell Claude:"
echo ""
echo "  \"Save context using the preservation template. Session: $SESSION_NUMBER, Task: $CURRENT_TASK\""
echo ""
echo "Claude will read the template and invoke context-manager automatically."
echo ""
echo "Next session will be: $((SESSION_NUMBER + 1))"

# Increment session counter for next time
echo "$((SESSION_NUMBER + 1))" > "$SESSION_COUNTER_FILE"
