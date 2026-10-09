---
name: architecture-check
description: Run architecture tests to validate Clean Architecture layer dependencies, naming conventions, and design constraints. Uses NetArchTest.
allowed-tools: Read, Bash, Grep, Glob
---

## Architecture Check

1. Run architecture tests:
   ```bash
   dotnet test --filter "Category=Architecture" --logger "console;verbosity=detailed"
   ```

2. If tests fail, analyze the violations:
   - **Layer dependency violation**: class in wrong layer or referencing forbidden layer
   - **Naming convention violation**: handler/validator/repository not following suffix rules
   - **Design constraint violation**: unsealed handler, public setter on entity, non-record value object

3. For each failure:
   - Identify the violating type and the rule it breaks
   - Suggest the specific fix (move to correct layer, rename, seal, etc.)
   - Reference `rules/architecture.md` for the constraint definitions
   - Reference `skills/testing-architecture/` for the test patterns

4. Re-run after fixes to confirm all architecture tests pass.
