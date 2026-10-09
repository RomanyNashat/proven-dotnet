---
name: lessons
description: The gotchas loop behind /lessons — capture what a session taught (corrections, taught steps, failures) as a report outside any repo, then fold each item into the harness one approved change at a time, so a mistake is made once.
version: 1.0.0
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
- **A failure:** an error, a stuck command, a misbehaving script or agent, a wrong assumption. Exact
  error text, and what fixed it (or that nothing did).
- **What worked:** parts of a flow that did their job, so a later change doesn't break them.

Not a lesson: the content of the work itself (what the feature does, what the handover said). Only how
Claude did it.

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

## 3. What broke or got stuck
### L3. …   (same fields, plus **Error:** the exact text, **Fixed by:** … or "still open")

## 4. What worked — keep as is
- <one line each; no harness change>

## 5. Open questions
- <where instructions conflicted, or Claude wasn't sure>
```

Numbering runs through the whole file (L1, L2, L3…), so an item can be referred to by its number.

**`Harness change` names a real file:** a command (`commands/handover.md`), an agent, a skill
(`skills/<name>/SKILL.md`, usually its `## Gotchas` section), a rule, `CLAUDE.md`, or a script. If it's
unclear which, write the best guess and say so.

### Rules for the file

- **No patient data, no secrets, credentials, tokens, connection strings or internal URLs.**
- **Neutral labels for services and repos** (Service A, Service B) unless the name is needed to
  understand the lesson. The file travels to the harness repo, which may be public.
- One lesson per item; specific and short. Don't merge two corrections into one item.
- Don't soften the evidence. "Claude ignored the rule twice" is more useful than "there was some
  confusion".

## 2. Fold in

Run in a session on the harness repo. The lessons files come from `~/.claude/lessons/` on that machine, or
are attached to the chat.

1. **Read every item with `Status: open`** across the files. Group items that point at the same file.
2. **Check again before anything enters the harness:** no service or repo names beyond neutral labels, no
   patient data, no internal URLs, no secrets. If an item can't be written without one, rewrite it
   generally or drop it.
3. **Walk the items one at a time, highest priority first.** For each: the lesson, the evidence, the exact change to the file (as a diff), and a recommendation (apply, change it, or drop it as a
   one-off). The developer approves, edits or skips.
4. **Apply the approved changes** on a branch, with the usual release notes; a skill's lesson usually
   becomes one line in its `## Gotchas` section (create the section at the end of the skill if it
   doesn't exist).
5. **Mark each item in its lessons file:** `Status: applied (vX.Y.Z)` or `Status: dropped (<reason>)`. A
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
