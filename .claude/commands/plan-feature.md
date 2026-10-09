---
name: plan-feature
description: Decompose a feature or epic into ordered tasks with acceptance criteria, complexity estimates, and agent assignments. Use at the start of any new feature work.
allowed-tools: Read, Write, Edit, Bash, Grep, Glob, Agent
---

Delegate to the **planner** agent.

1. Read the feature description provided by the user
2. Grep/Read existing codebase to understand current patterns
3. Decompose into ordered, atomic tasks with acceptance criteria
4. Assign each task to the appropriate agent (tdd-guide, architect, etc.)
5. Reference relevant skills for each task
6. Identify risks, unknowns, and prerequisites

Output a structured implementation plan following the format in `agents/planner.md`.
