#!/bin/bash
# load-context.sh - Helper to invoke context-manager for context restoration
# Usage: ./load-context.sh [session_number]
#
# ⚠️ DEPRECATED: This script is deprecated in favor of the /load-context slash command.
# Please use: /load-context [session_number]
# This script remains available as a fallback for manual invocation.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
TEMPLATE_FILE="$PROJECT_ROOT/.claude/templates/RESTORATION_PROMPT.md"
CONTEXT_DIR="$PROJECT_ROOT/.claude/context/active"

SESSION_NUMBER="${1:-}"

# Find session file
if [ -z "$SESSION_NUMBER" ]; then
    # Find most recent session
    SESSION_FILE=$(ls -t "$CONTEXT_DIR"/session_*.md 2>/dev/null | head -1)
    if [ -z "$SESSION_FILE" ]; then
        echo "❌ No session files found in $CONTEXT_DIR"
        echo ""
        echo "Available sessions:"
        ls -1 "$CONTEXT_DIR"/*.md 2>/dev/null || echo "  (none)"
        exit 1
    fi
else
    # Find specific session
    SESSION_FILE=$(ls -t "$CONTEXT_DIR"/session_${SESSION_NUMBER}_*.md 2>/dev/null | head -1)
    if [ -z "$SESSION_FILE" ]; then
        echo "❌ Session $SESSION_NUMBER not found"
        echo ""
        echo "Available sessions:"
        ls -1 "$CONTEXT_DIR"/session_*.md 2>/dev/null | sed 's/.*session_/  Session /' | sed 's/_.*//'
        exit 1
    fi
fi

# Read template
if [ ! -f "$TEMPLATE_FILE" ]; then
    echo "❌ Template not found: $TEMPLATE_FILE"
    exit 1
fi

# Build prompt by substituting session file path
PROMPT=$(cat "$TEMPLATE_FILE" | sed "s|{SESSION_FILE}|$SESSION_FILE|g")

echo "=========================================="
echo "Context Restoration"
echo "=========================================="
echo ""
echo "Session file: $SESSION_FILE"
echo ""
echo "To restore context, use the following prompt with context-manager agent:"
echo ""
echo "---"
echo "$PROMPT"
echo "---"
echo ""
echo "Or, if you're in Claude Code CLI, tell Claude:"
echo ""
echo "  \"Load context from session file: $SESSION_FILE\""
echo ""
echo "Claude will read the restoration template and invoke context-manager automatically."
echo ""
echo "Quick preview:"
echo ""
head -30 "$SESSION_FILE"
echo ""
echo "... (see full file at $SESSION_FILE)"
