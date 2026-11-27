---
name: plan-feature
description: "Break a feature or epic into ordered tasks with acceptance criteria and estimates in hours."
allowed-tools: Read, Write, Edit, Bash, Grep, Glob, Agent
---

Delegate to the **planner** agent.

1. Read the feature description provided by the user
2. Grep/Read existing codebase to understand current patterns
3. Decompose into ordered, atomic tasks with acceptance criteria
4. Assign each task to the appropriate agent (tdd-guide, architect, etc.)
5. Reference relevant skills for each task
6. Identify risks, unknowns, and prerequisites
7. Estimate each task in hours, best / likely / worst, and run `pert.py` (`problem-analysis`): the plan promises its 80% figure
8. Check the plan with `plan_check.py` (`story-check`): scope against the story, design against what the repo has, the API as its callers see it

Output a structured implementation plan following the format in `agents/planner.md`.
