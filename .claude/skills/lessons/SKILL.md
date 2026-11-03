---
name: lessons
description: The gotchas loop behind /lessons — capture what a session taught (corrections, taught steps, procedures the developer walked Claude through, failures) as a report outside any repo, then fold each item into the harness one approved change at a time, so a mistake is made once and a procedure is taught once.
version: 1.2.0
---

# Lessons — turn a session's corrections into harness changes

The harness (proven-dotnet, plus any team layer on top) gets better the same way every time: Claude does something wrong, the developer corrects it, and the
correction becomes a rule, a check or a gotcha so it doesn't happen again. Done by memory, most
corrections are lost when the session ends. This skill makes it a loop with two halves:

1. **Capture**, at the end of the session where it happened: a report of what was learned. Nothing in the harness changes.
2. **Fold in**, later, in a session on the harness repo: each item becomes an approved change, or is
   dropped with a reason. A lesson about proven-dotnet itself can go upstream as a pull request; a
   lesson about your team's ways goes into your team's layer.

The two halves are separate on purpose. The session that made the mistake is the one that remembers it,
but it is usually in a work repo, where the harness must not be edited.

## 1. Capture

### Where the report goes

`~/.claude/lessons/YYYY-MM-DD-<slug>.md`, where `<slug>` is a few words about the session
(`handover-lessons`, `payment-refund-feature`). **Never inside a repo:** a lessons file is about how
Claude worked, not about the code, and it must not be committed by accident. The installer never touches this folder.

### What counts as a lesson

Go through the whole session, including anything saved to memory or notes:
- **A correction:** Claude did X, the developer wanted Y. Quote the instruction briefly as evidence.
- **A taught step:** the developer added something to the flow (an order, a check, a question to ask,
  a step to skip).
- **A taught procedure:** the developer walked Claude through how to make something the team makes
  again and again: a deployment document, a release package, a report, a migration. Capture the
  procedure itself, whole, not the fact that it happened: what it needs and where each input comes
  from, the steps in order, the shape of the result (sections, tables, folders, file names), the
  checks before it goes out, and the corrections made along the way. If the developer gave Claude a
  page, a template or an earlier example to work from, say which (by name, not by link) and what
  Claude took from it. This is the most valuable item in a report: a correction stops one mistake, a
  procedure saves the whole job next time.
- **A failure:** an error, a stuck command, a misbehaving script or agent, a wrong assumption. Exact
  error text, and what fixed it (or that nothing did).
- **What worked:** parts of a flow that did their job, so a later change doesn't break them.

Not a lesson: the content of the work itself (what the feature does, what the handover said). Only how
Claude did it, and how the developer showed it the job should be done. **A procedure is not "content"**:
leaving one out because it looks like the work itself is the mistake this rule is most likely to cause.

### The report

```markdown
# Lessons: <session in a few words>
Date: YYYY-MM-DD · Layers: <from /version> · Where: <repo label, e.g. "Service A">

## 1. Corrections
### L1. <one line>
- **Did:** … **Wanted:** … **Rule behind it:** …
- **Evidence:** "<the developer's words, short>"
- **Harness change:** `<file>`: <the change in one or two lines>
- **Priority:** must | should | nice
- **Status:** open

## 2. Steps taught
### L2. …   (same fields)

### L3. Procedure: <what it makes, e.g. "the deployment document">
- **When:** <the moment it's needed, e.g. "before a release goes to production">
- **Inputs:** <each input, and where it comes from: a branch compare, a ticket, a template, a person>
- **Steps:** <numbered, in order, each a thing to do, with the command or check where there is one>
- **Result:** <its structure: sections or table columns in order, folder layout, file names>
- **Checks before it goes out:** <what was verified, and how>
- **Corrections made on the way:** <what the developer changed, with short evidence>
- **Worked example:** <one, with sensitive values replaced; names kept only if the layer is private>
- **Harness change:** <usually a skill: a section in an existing one, or a new one; a company
  procedure goes to the team layer>
- **Priority:** must | should | nice
- **Status:** open

## 3. What broke or got stuck
### L3. …   (same fields, plus **Error:** the exact text, **Fixed by:** … or "still open")

## 4. What worked — keep as is
- <one line each; no harness change>

## 5. Open questions
- <where instructions conflicted, or Claude wasn't sure>
```

Numbering runs through the whole file (L1, L2, L3…), so an item can be referred to by its number. A
procedure is one item, however long; it never shrinks to a single "check first"-style line.

**`Harness change` names a real file:** a command (`commands/handover.md`), an agent, a skill
(`skills/<name>/SKILL.md`, usually its `## Gotchas` section), a rule, `CLAUDE.md`, or a script. If it's
unclear which, write the best guess and say so.

### Rules for the file

- **No patient data, no secrets, credentials, tokens, connection strings or internal URLs.**
- **Neutral labels for services and repos** (Service A, Service B) unless the name is needed to
  understand the lesson. The file travels to the harness repo, which may be public. A procedure that
  belongs to the team layer (a private repo) may keep the team's own names for its templates, folders
  and tools; still no secrets, patient data or internal URLs.
- One lesson per item; specific and short. Don't merge two corrections into one item.
- Don't soften the evidence. "Claude ignored the rule twice" is more useful than "there was some
  confusion".

## 2. Fold in

Run in a session on the harness repo. The lessons files come from `~/.claude/lessons/` on that machine, or
are attached to the chat.

1. **Read every item with `Status: open`** across the files. Group items that point at the same file.
2. **Check again before anything enters the harness:** no service or repo names beyond neutral labels
   (a private team layer may keep its own template and tool names), no patient data, no internal URLs,
   no secrets. If an item can't be written without one, rewrite it generally or drop it.
3. **Walk the items one at a time, highest priority first.** For each: the lesson, the evidence, the exact change to the file (as a diff), and a recommendation (apply, change it, or drop it as a
   one-off). The developer approves, edits or skips.
4. **Apply the approved changes** on a branch, with the usual release notes; a skill's lesson usually
   becomes one line in its `## Gotchas` section (create the section at the end of the skill if it
   doesn't exist). **A procedure becomes skill content**, not a gotcha: a section in the skill for that
   job (its steps, the result's structure, the checks), or a new skill when none fits. Show the developer
   the whole section before applying; a procedure written from the report alone misses what only the
   original session saw, so check it against the template or example the developer used.
5. **Gate what the fix evals cover.** A change to a rule, an agent, or a skill a fix eval's findings cite
   (`tests/evals/fix-*/evals.md`, "Where it's taught") is kept only if no case gets worse. Run the
   with-proven arm again on the branch, as many runs as the baseline has, add the copies under
   `runs/`, and commit `scores.json` from `tools/gate.py scores`. `fix-evals.yml` grades them and fails
   the PR when any case, finding or regression, passes in fewer runs than on `main`. A plausible lesson
   that costs another case is how a harness gets worse while every change looks right. Approval by the
   developer is the second check, not the only one.
6. **Mark each item in its lessons file:** `Status: applied (vX.Y.Z)` or `Status: dropped (<reason>)`. A
   file whose items are all closed is done; keep it, it's the history of why the gotcha exists.

### Gotchas sections

One line per gotcha: what goes wrong, then what to do instead.

```markdown
## Gotchas
- `sp_` procedures that return two result sets: Dapper's `Query` reads only the first. Use `QueryMultiple`.
- The parity gate's characterization test must run before the first edit, not after.
```

A gotcha that keeps growing into a paragraph belongs in the body of the skill, not the list.

### What not to fold in

- A one-off: something specific to one repo or one day. Drop it with that reason.
- A preference that belongs in the developer's memory rather than in the harness for everyone.
- Anything that would make a rule less safe or less honest (skipping tests, skipping review, hiding a
  failure) because it was faster in one session. Drop it and say why.
