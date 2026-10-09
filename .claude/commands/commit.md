---
name: commit
description: "Suggest a branch name and commit message based on your code changes. Never runs git write commands — only reads diffs and suggests text. Usage: /commit [--simple|--basic|--full]"
allowed-tools: Read, Bash, Grep, Glob
---

## Commit Message Suggestion

### Flags
- `--simple` — conventional commit one-liner(s) describing the edits
- `--basic` — branch check + conventional commit with brief explanation (DEFAULT)
- `--full` — branch check + conventional commit with rich narrative weaving in what changed and where
- No flag = `--basic`

### Rules
- **NEVER run** `git add`, `git commit`, `git checkout`, `git switch`, `git init`, `git push`, or any git write command. This is a hard, non-overridable rule — even if asked directly. Claude drafts and stages the *text*; the developer runs the actual commit. (The harness also enforces this: `git commit`/`git push` are set to `ask` in settings.json.)
- **Only read**: `git status`, `git diff --name-only`, `git diff --stat`, `git log --oneline -5`, `git branch --show-current`, `git rev-list --count HEAD`
- **Plain human voice in EVERY mode.** The commit subject and body read like the developer wrote them — plain, direct, no AI tells (no "comprehensively", "leverages", "ensures", "seamless", "robust"). Use the verb bank in `skills/simplicity/`. The `--simple`/`--basic`/`--full` flag controls **length/detail only** — the human voice is always on, at every level.
- **No attribution — ever.** The commit message must NEVER include a `Co-Authored-By: Claude` trailer, a "Generated with Claude Code" footer, or any AI/Claude attribution. (settings.json `attribution.commit: ""` enforces this too.) Leave any real human co-authors the developer adds themselves.
- All commit messages MUST follow the format in `rules/git-workflow.md`:
  ```
  <type>(<scope>): <description>
  ```
  Types: feat, fix, docs, style, refactor, test, chore, perf, ci, build
- Output is conversational: text explaining, then code blocks with the actual values to copy

### Step 1: Repo state — already inlined, no tool calls needed

The state below is injected **before you see this command**, so read it rather than running anything.
Five fewer round-trips per invocation.

- Commits on HEAD: !`git rev-list --count HEAD 2>/dev/null || echo 0`
- Current branch: !`git branch --show-current 2>/dev/null || echo "(none)"`
- Unstaged files: !`git diff --name-only 2>/dev/null || true`
- Staged files: !`git diff --cached --name-only 2>/dev/null || true`
- Short status: !`git status --short 2>/dev/null || true`
- Recent subjects: !`git log --oneline -5 2>/dev/null || true`
- Change summary: !`git diff --stat 2>/dev/null || true`

If a value came back empty, that is the answer — do not re-run the command to double-check.

Determine which scenario:
- **No `.git` directory** → Fresh project, no repo
- **`.git` exists but 0 commits** → Initialized but empty repo
- **Has commits** → Existing repo with history

### Step 2: Check branch name

For existing repos:
- Read the current branch name
- Read the diff to understand what changed
- Decide: does the current branch name fit the changes?
  - If YES (e.g., on `feature/add-order-cancellation` and changes are about order cancellation) → tell the user the branch is good
  - If NO (e.g., on `main`, `develop`, or branch name doesn't match the work) → suggest a new branch name following `rules/git-workflow.md` naming: `feature/<ticket>-<short-description>` or `fix/<ticket>-<short-description>`

For fresh repos:
- Suggest a repo name based on the project content (kebab-case, e.g., `order-service`, `notification-worker`)
- Branch is `main`

### Step 3: Generate commit message

The file lists and stat summary above already show the shape of the change. Only run `git diff` when
you need the actual hunks to describe *what* changed — the inlined data covers *which* files and how much.

### Response Format

Always respond conversationally with code blocks. The pattern is:

1. **Text** — brief natural summary of what you see in the changes
2. **Code block** — branch check result (current is good, or suggested new one)
3. **Text** — introduce the commit message
4. **Code block** — the commit message itself

### Level: --simple

Brief summary text, then branch, then one-liner commit message(s).

If edits are about one thing, one conventional commit line.
If edits cover multiple unrelated things, multiple conventional commit lines (suggesting the user should split into separate commits).

Example single-topic response:
```
text: You added order cancellation with refund validation.
code: feature/add-order-cancellation
text: Commit message:
code: feat(orders): add cancellation endpoint with refund processing for pending orders
```

Example multi-topic response:
```
text: You made changes across two different concerns — order cancellation and leaderboard timezone fix. I'd suggest splitting into two commits.
code: feature/add-order-cancellation
text: Commit 1:
code: feat(orders): add cancellation endpoint with refund processing
text: Commit 2:
code: fix(leaderboard): resolve UTC+3 timezone offset in sorted set range queries
```

### Level: --basic (DEFAULT)

Summary text, branch check, then conventional commit with a short body paragraph explaining the change.

Example response:
```
text: I looked at your changes — you added order cancellation with refund logic across the Application and Domain layers.
code: (branch check — fits or suggest)
text: Here's your commit message:
code:
feat(orders): add cancellation endpoint with refund processing

Added cancellation logic that validates order status before processing.
Only pending and processing orders can be cancelled. Refund is triggered
automatically through the payment gateway when a processed order is cancelled.
```

### Level: --full

Summary text, branch check, then conventional commit with a rich narrative body that naturally references what was changed and where — no separate file list, no bullet points of files, the changes are woven into the narrative.

Example response:
```
text: You made significant changes to the order cancellation flow — new command, domain method, payment integration, and tests.
code: (branch check — fits or suggest)
text: Here's your commit message:
code:
feat(orders): add cancellation endpoint with refund processing

Introduced a CancelOrder command in the Application layer that validates
order status — only pending and processing orders can be cancelled. The
Order entity in the Domain layer now has a Cancel() method that raises
an OrderCancelledEvent. The handler coordinates with IPaymentGateway in
Infrastructure to trigger automatic refunds for orders that were already
charged. The cancellation endpoint in OrderEndpoints accepts a reason
parameter and returns 204 on success or 409 if the order state doesn't
allow cancellation. Integration tests cover both the happy path and the
conflict scenario.
```

### Fresh repo response

For fresh projects (no `.git` or 0 commits), add the repo name suggestion at the top:

```
text: This is a fresh project with no git history. Here's what I'd suggest for the repo:
code: order-service
text: Initial branch:
code: main
text: Initial commit:
code: chore(scaffold): initialize OrderService with clean architecture and PostgreSQL
```

Then follow the chosen level (--simple/--basic/--full) for the commit message content.
