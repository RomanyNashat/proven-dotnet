# Git Workflow

Defaults. Where your team's rules say otherwise (another branch model, a different commit format),
the team's rules win.

## Commit Format
```
<type>(<scope>): <description>

[optional body]
[optional footer]
```

**Types**: feat, fix, docs, style, refactor, test, chore, perf, ci, build
**Scope**: service name or module (optional but encouraged)
**Description**: imperative mood, lowercase, no period, max 72 chars

Examples:
- `feat(notifications): add push bulk chunking with 500-item limit`
- `fix(orders): resolve UTC+3 off-by-one in sorted set range queries`
- `refactor(handlers): extract decorator registration to extension method`
- `perf(catalog-api): fix connection leak in nested Task.Run`

## Branches
- Branch from the main branch for every change: `feat/<short-description>`, `fix/<short-description>`
  (with the issue number if there is one).
- One change per branch, merged through a pull request (a merge request on GitLab).
- Delete the branch after merge unless your team keeps them for traceability.

**Beware re-merging reverted commits.** If a commit was reverted on a shared branch and the original
branch is merged again, git considers it already merged and the change silently does not come back.
When a revert is involved, re-apply the change on a new commit rather than re-merging.

## Claude and Git
- **Claude doesn't commit or push on its own initiative.** It drafts the commit message and asks; it
  commits when asked, and pushes only when asked. To make this a hard stop, set `git commit` and
  `git push` to `ask` in your permissions.
- **Never rewrite shared history:** no force-push, `reset --hard` or rebase of a branch others use.
- **Plain human voice** in commit subject and body — no AI tells; see `skills/simplicity/`.
- AI attribution trailers follow your settings (`attribution` in settings.json).

## Rules
- Every commit must build and pass tests — no broken commits on shared branches.
- Never force-push to the main branch or any shared branch.
- Re-apply, never re-merge, a change whose commit was reverted.
- PR title follows the commit format: `feat(scope): description`.

## PR Process
1. Self-review the diff before requesting review.
2. PR description includes: what changed, why, how to test, breaking changes.
3. Link the related issue.
4. All CI checks must pass before merge.
5. At least one approval required (code-reviewer agent for solo work).
