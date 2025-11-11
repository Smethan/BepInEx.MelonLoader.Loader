#!/bin/bash

# PreToolUse hook for Bash commands
# Detects PR creation and saves context with milestone flag

# Read hook input from stdin
HOOK_INPUT=$(cat)

# Extract the command from the hook input JSON
# The command is in tool_input.command field
COMMAND=$(echo "$HOOK_INPUT" | python3 -c '
import json, sys
try:
    data = json.load(sys.stdin)
    if "tool_input" in data and "command" in data["tool_input"]:
        print(data["tool_input"]["command"])
except:
    pass
' 2>/dev/null)

# Check if this is a PR creation command
if echo "$COMMAND" | grep -q "gh pr create"; then
    echo "🔖 Detected PR creation command. Saving milestone context..." >&2

    # Run save-context with milestone flag via the slash command script
    # We need to invoke the save-context logic with milestone flag
    SAVE_RESULT=$(bash "$CLAUDE_PROJECT_DIR/.claude/scripts/save-context.sh" --milestone 2>&1)

    # Output JSON with systemMessage to inform Claude
    cat <<'EOF'
{
  "hookSpecificOutput": {
    "hookEventName": "PreToolUse",
    "systemMessage": "🔖 Milestone context saved before PR creation. Proceeding with pull request..."
  }
}
EOF
else
    # Not a PR command, just pass through silently
    echo "{}"
fi
