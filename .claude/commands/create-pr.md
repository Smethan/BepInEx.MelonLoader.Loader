---
description: Create a pull request with automatic milestone context preservation
---

# Create Pull Request with Milestone Save

Create a pull request while automatically preserving context as a milestone.

## Instructions

You MUST follow this workflow when the user asks to create a pull request:

### Step 1: Save Milestone Context

**BEFORE creating the PR**, run the `/save-context --milestone` command to preserve the current state as a milestone. This will:
- Save session state with milestone flag
- Update PROJECT_STATUS.md with current ADRs and version
- Create a permanent record of this release/feature completion

Wait for the `/save-context --milestone` command to complete successfully before proceeding.

### Step 2: Verify Git State

Check the current branch and ensure all changes are committed:
```bash
git status
git log -1
git branch --show-current
```

### Step 3: Create the Pull Request

Follow the standard PR creation workflow:
1. Check if branch needs to be pushed: `git status`
2. Push to remote with `-u` flag if needed: `git push -u origin <branch-name>`
3. Create PR using `gh pr create` with proper title and body
4. Include summary of changes from the milestone context save

### Step 4: Report Success

After PR creation:
- Show the PR URL to the user
- Confirm that milestone context was saved
- Mention the session number from the context save

## Example Usage

When user says:
- "Create a pull request"
- "Make a PR for this"
- "Open a pull request"

You should:
1. Run `/save-context --milestone`
2. Wait for completion
3. Gather git information
4. Create PR with `gh pr create`
5. Report back with PR URL and confirmation

## Expected Output

```
🔖 Saving milestone context before PR creation...
✅ Milestone saved: session_N_TIMESTAMP.md

📊 Creating pull request...
✅ PR created: https://github.com/user/repo/pull/123

Summary:
- Milestone context saved (session N)
- PROJECT_STATUS.md updated
- PR #123 created and ready for review
```

## Notes

- This ensures every PR has associated milestone documentation
- Milestone saves include PROJECT_STATUS.md updates
- Context preservation happens BEFORE PR creation
- If `/save-context --milestone` fails, ask user before proceeding with PR
