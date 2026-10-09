---
name: document
description: "Write wiki/README documentation for a .NET service, grounded in its real code. Reads the service (endpoints, dependencies, config, jobs) and produces a clear page. Asks simple or full first (simplicity layer). For onboarding docs, service READMEs, and platform-wiki pages."
allowed-tools: Read, Bash, Grep, Glob, Agent
---

## Document a service

Uses the `service-documentation` skill. Reads a .NET service and writes the wiki/README page a
teammate would read to understand it — grounded in the **real code**, in plain language.

### Flow

```
/document [path-to-service]
  │
  ├── Step 0: Point at the service   (a path, or the current repo)
  │
  ├── Step 1: Simple or full?        (simplicity layer — ask first)
  │     ├── simple → the essentials, terse (default lean; good for most READMEs)
  │     └── full   → + per-endpoint shapes, key-flow sequence, deeper config/architecture
  │
  ├── Step 2: READ the service       (prefer proven-roslyn MCP; fall back to files)
  │     └── purpose, API/endpoints, dependencies, config, background jobs, run-locally, gotchas
  │
  ├── Step 3: WRITE the page         (Markdown, plain language, real names)
  │
  └── Step 4: REVIEW — the self-check gate   (doc-review skill, always runs)
        ├── A. Groundedness — trace EVERY claim (endpoints, deps, config, jobs) back to source;
        │      an untraceable claim is a fabrication → remove or correct it
        ├── B. Completeness — reverse sweep: what exists in code but is missing from the doc
        ├── C. Voice rules — simplicity voice, no filler sections, gotchas real not padded
        └── auto-fix, then present with the PRE-FIX grade + every correction + anything
               that couldn't be verified (flagged for you, never silently kept or dropped)
```

### What it produces
A Markdown page with the sections that apply to the service: Overview, What it does, API/endpoints,
Dependencies, Configuration, Running locally, Background jobs/consumers, and — the valuable one —
Gotchas/notes. Inapplicable sections are dropped (a worker has no API; a stateless read API has no
consumers).

### Rules
- **Grounded in the code** — never invents endpoints, dependencies, or config; if it can't be read,
  it asks or says so.
- **Simplicity layer** — asks simple or full first; plain language, real names, no AI tells throughout.
- **Adapts to the service** — drops sections that don't apply; doesn't pad. A shorter true page beats
  a long templated one.
- **Plan-first** — presents the doc; the developer decides where it goes.
- **Always reviewed** — the `doc-review` gate runs before you see the page. A wiki page is trusted by
  whoever reads it, so a confidently-stated endpoint that doesn't exist is the worst failure mode; the
  gate verifies every claim against source rather than judging its own prose.
